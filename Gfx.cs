using System.Numerics;
using Raylib_cs;

namespace MiniGTA;

/// <summary>
/// GPU layer.
///  - Box / BoxQ / CylQ / Cone : lit by the "world" shader (PBR-ish look: moon light, street lamps, casino neon,
///    procedural facades / asphalt / sand, specular, sky reflections, fog).
///  - Flat / FlatQ              : emissive objects (neon, lamps, lights), only fogged.
///  - DrawSky                   : sky dome with gradient, stars and moon.
///  - BeginScene / EndScene     : the 3D scene is rendered into a texture and then run through the
///                                post-process shader (FXAA, bloom, vignette, grain).
/// </summary>
public static unsafe class Gfx
{
    /// <summary>Material ids understood by the world shader.</summary>
    public static class M
    {
        public const int Generic = 0, Sand = 1, Asphalt = 2, Walk = 3, Building = 4, Paint = 5,
            Glass = 6, Rubber = 7, Metal = 8, CasinoWall = 9, Mountain = 10, Foliage = 11;
    }

    static Model lit, flat, cyl, cone, sky;
    static Shader litSh, flatSh, skySh, postSh;

    static int viewLoc = -1, flatViewLoc = -1, skyViewLoc = -1;
    static int matLoc = -1, seedLoc = -1, timeLoc = -1;
    static int neonPosLoc = -1, neonColLoc = -1, carPosLoc = -1, carDirLoc = -1;
    static int postResLoc = -1, postTimeLoc = -1;

    static int curMat = -1;
    static float curSeed = float.NaN;

    static RenderTexture2D rt;
    static bool rtOk;
    static int rtW, rtH;

    // ---- user models (folder "models": car*, player*, npc*, cop_car*, cop_npc*  .obj .glb .gltf .iqm .vox .m3d)
    public const int CatCar = 0, CatPlayer = 1, CatNpc = 2, CatCopCar = 3, CatCopNpc = 4;
    class Custom
    {
        public Model Mdl; public Vector3 Off; public float Scale, BaseYaw;
        public Quaternion Tilt = Quaternion.Identity;           // applied to the raw model first (stands lying-down characters upright)
        public int Kind;                                         // cars: CarKind from the file name (car_bus_1.glb -> Bus)
        public Model[] Frames;                                   // baked animation frames (views into Mdl), null = static model
        public Dictionary<string, (int Start, int Count)> Clips;  // clip name -> first frame, frame count
        // skeletal animation straight from the .glb/.gltf/.iqm/.m3d (no .anim sheet needed)
        public List<ModelAnimation> Anims;                       // raylib animations (memory owned by raylib, never freed)
        public Dictionary<string, int> Skel;                     // canonical clip name (idle/walk/run...) -> index in Anims
        public int LastAnim = -1, LastFrame = -1;                // what is currently skinned into the mesh (skip redundant work)
        public string BasePath;
    }

    /// <summary>Offset of the weapon grip between the old box-body and the baked 'aim' pose of the player model.</summary>
    public static Vector3 GunShift;
    static readonly List<Custom>[] custom =
    {
        new List<Custom>(), new List<Custom>(), new List<Custom>(),
        new List<Custom>(), new List<Custom>()
    };
    // default extra yaw per category id: car, player, npc, cop_car, cop_npc. cop_car.glb faces backwards -> 180.
    static readonly float[] yawFix = { 0f, 0f, 0f, 180f, 0f };
    static readonly HashSet<string> loadedPaths = new();
    static readonly string[] Exts = { ".obj", ".glb", ".gltf", ".iqm", ".vox", ".m3d" };
    // IMPORTANT: longer prefixes (cop_car, cop_npc) come first so they win over the bare "car"/"npc" prefixes.
    static readonly string[] CatNames = { "cop_car", "cop_npc", "car", "player", "npc" };
    // Map a CatNames index to a category id. Without this, "cop_car" (index 0) would be stored as category 0 (CatCar).
    static readonly int[] CatIds = { CatCopCar, CatCopNpc, CatCar, CatPlayer, CatNpc };
    // Display name for each category id (used by RotateFix). Indexed by category id, NOT by CatNames index.
    static readonly string[] CatDisplay = { "car", "player", "npc", "cop_car", "cop_npc" };

    static readonly int[] nIdx = new int[4];
    static readonly float[] nD = new float[4];

    // ------------------------------------------------------------------------------------------ shaders
    const string Vs = """
#version 330
in vec3 vertexPosition;
in vec3 vertexNormal;
in vec2 vertexTexCoord;
uniform mat4 mvp;
uniform mat4 matModel;
out vec3 fragPos;
out vec3 fragNormal;
out vec2 fragUV;
void main()
{
    fragUV = vertexTexCoord;
    fragPos = vec3(matModel * vec4(vertexPosition, 1.0));
    fragNormal = normalize(mat3(matModel) * vertexNormal);
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
""";

    const string Fs = """
#version 330
in vec3 fragPos;
in vec3 fragNormal;
in vec2 fragUV;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec3 viewPos;
uniform int matId;
uniform float seed;
uniform float time;
uniform vec3 neonPos[4];
uniform vec3 neonCol[4];
uniform vec4 carPos;
uniform vec3 carDir;
out vec4 finalColor;

const float STEP = 82.0;
const float WORLD = 984.0;   // must match World.Max = N * Step (N=12 -> 12*82=984)
const float LO = 9.4;
const vec3 MOON = vec3(-0.618, 0.459, -0.638);
const vec3 HORIZON = vec3(0.040, 0.025, 0.042);

float hash21(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p)
{
    float s = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        s += a * vnoise(p);
        p = p * 2.03 + vec2(17.1, 9.2);
        a *= 0.5;
    }
    return s;
}

vec3 skyCol(vec3 d)
{
    float h = clamp(d.y, 0.0, 1.0);
    vec3 c = mix(HORIZON, vec3(0.010, 0.014, 0.040), pow(h, 0.4));
    return mix(c, vec3(0.002, 0.003, 0.012), smoothstep(0.3, 1.0, h));
}

vec3 display(vec3 c)
{
    c = 1.0 - exp(-c * 2.0);
    return pow(c, vec3(1.0 / 2.2));
}

// ---- accumulated lighting
vec3 gDiff = vec3(0.0);
vec3 gSpec = vec3(0.0);
vec3 gN = vec3(0.0, 1.0, 0.0);
vec3 gV = vec3(0.0, 0.0, 1.0);
float gShin = 16.0;

void addPoint(vec3 lp, vec3 lc, float radius)
{
    vec3 d = lp - fragPos;
    float dist = length(d);
    if (dist >= radius) return;
    vec3 L = d / dist;
    float att = 1.0 - dist / radius;
    att *= att;
    float ndl = max(dot(gN, L), 0.0);
    gDiff += lc * ndl * att;
    vec3 H = normalize(L + gV);
    gSpec += lc * pow(max(dot(gN, H), 0.0), gShin) * att * step(0.001, ndl);
}

void lamp(vec2 xz)
{
    if (xz.x < -13.0 || xz.y < -13.0 || xz.x > WORLD + 13.0 || xz.y > WORLD + 13.0) return;
    addPoint(vec3(xz.x, 7.4, xz.y), vec3(2.2, 1.35, 0.66), 30.0);
}

// street lamps stand on the sidewalk corners and in the middle of every block edge
void streetLamps()
{
    vec2 p = fragPos.xz;
    vec2 g = floor(p / STEP + 0.5) * STEP;
    vec2 m = (floor(p / STEP) + 0.5) * STEP;
    for (int a = -1; a <= 1; a += 2)
    {
        for (int b = -1; b <= 1; b += 2) lamp(g + vec2(float(a), float(b)) * LO);
        lamp(vec2(g.x + float(a) * LO, m.y));
        lamp(vec2(m.x, g.y + float(a) * LO));
    }
}

// road markings: 'across' = distance across the road from its centre line, 'along' = distance from the crossing
float roadMark(float across, float along, out vec3 mc)
{
    float aa = abs(along);
    float ac = abs(across);
    float yel = step(13.8, aa) * step(abs(ac - 0.3), 0.09);
    float edge = step(13.8, aa) * step(abs(ac - 8.1), 0.1);
    float zebra = step(10.0, aa) * step(aa, 13.0) * step(ac, 8.3) * step(0.5, fract(across / 1.7));
    mc = (yel > 0.5) ? vec3(0.55, 0.38, 0.03) : vec3(0.42, 0.42, 0.40);
    return max(yel, max(edge, zebra));
}

void main()
{
    vec3 n = normalize(fragNormal);
    vec3 V = normalize(viewPos - fragPos);
    float dist = length(viewPos - fragPos);
    float wall = 1.0 - step(0.5, abs(n.y));
    float nearK = 1.0 - clamp(dist / 140.0, 0.0, 1.0);
    vec2 q = fragPos.xz;

    vec3 albedo = pow(colDiffuse.rgb, vec3(2.2));
    vec3 emis = vec3(0.0);
    float spec = 0.12;
    float shin = 20.0;
    float refl = 0.0;
    vec3 reflTint = vec3(1.0);
    float ao = 1.0;
    float fogCap = 1.0;

    if (matId == 0)                       // generic / user models: use the texture if there is one
    {
        vec4 tx = texture(texture0, fragUV);
        if (tx.a < 0.5) discard;                 // hair cards / cut-outs
        albedo *= pow(tx.rgb, vec3(2.2));
    }
    else if (matId == 1)                  // desert sand
    {
        albedo *= 0.6 + 0.8 * fbm(q * 0.09);
        albedo *= 0.9 + 0.2 * mix(0.5, hash21(floor(q * 5.0)), nearK);
        spec = 0.0;
    }
    else if (matId == 2)                  // asphalt with markings, wear and wet patches
    {
        albedo *= 0.7 + 0.6 * fbm(q * 0.30);
        albedo *= 0.88 + 0.24 * mix(0.5, hash21(floor(q * 9.0)), nearK);
        float top = step(0.5, abs(n.y));
        float wet = smoothstep(0.56, 0.72, fbm(q * 0.045 + 31.0)) * top;

        vec2 g = q - floor(q / STEP + 0.5) * STEP;
        vec3 mc = vec3(0.0);
        float mk = 0.0;
        if (abs(g.x) < 9.0 && abs(g.y) >= 9.0) mk = roadMark(g.x, g.y, mc);
        else if (abs(g.y) < 9.0 && abs(g.x) >= 9.0) mk = roadMark(g.y, g.x, mc);
        mk *= top * (0.55 + 0.45 * smoothstep(0.25, 0.65, vnoise(q * 1.7)));

        albedo *= 1.0 - 0.45 * wet;
        albedo = mix(albedo, mc, mk);
        spec = 0.3 + 0.9 * wet;
        shin = 50.0 + 220.0 * wet;
        refl = (0.08 + 0.5 * wet) * (1.0 - mk);
    }
    else if (matId == 3)                  // concrete sidewalk tiles
    {
        vec2 t = q / 1.9;
        vec2 ft = fract(t);
        float gl = max(step(ft.x, 0.03), step(ft.y, 0.03));
        albedo *= (0.8 + 0.4 * hash21(floor(t))) * (0.85 + 0.3 * fbm(q * 0.6));
        albedo *= 1.0 - 0.4 * gl * nearK;
        spec = 0.06;
        shin = 12.0;
    }
    else if (matId == 4 || matId == 9)    // building facades / casinos
    {
        bool cas = (matId == 9);
        if (wall > 0.5)
        {
            float u = abs(n.x) > 0.5 ? fragPos.z : fragPos.x;
            float salt = abs(n.x) > 0.5 ? 7.0 : 0.0;
            vec2 cell = vec2(u / 3.0, fragPos.y / 3.6);
            vec2 id = floor(cell);
            vec2 f = fract(cell);
            bool ground = id.y < 0.5;
            float wx0 = ground ? 0.05 : 0.15;
            float wy0 = ground ? 0.10 : 0.22;
            float win = smoothstep(wx0, wx0 + 0.04, f.x) * (1.0 - smoothstep(0.96 - wx0, 1.0 - wx0, f.x))
                      * smoothstep(wy0, wy0 + 0.04, f.y) * (1.0 - smoothstep(0.80, 0.84, f.y));

            vec2 rid = id + vec2(salt * 3.1, seed * 91.0);
            float r = hash21(rid);
            float lit = 1.0 - step(cas ? 0.80 : 0.50, r);
            float tv = step(0.78, hash21(rid * 1.37 + 5.0));
            vec3 wc = mix(vec3(1.0, 0.72, 0.38), vec3(0.62, 0.78, 1.0), tv) * (0.55 + 0.9 * hash21(rid + 11.0));
            if (ground)
            {
                wc = vec3(1.0, 0.78, 0.45) * 1.3;
                lit = cas ? 1.0 : 1.0 - step(0.80, r);
            }
            float blind = smoothstep(0.35, 0.8, f.y) * step(0.5, hash21(rid + 7.0));
            wc *= 1.0 - 0.65 * blind;

            float seam = max(step(f.y, 0.025), step(f.x, 0.01));
            albedo *= (0.85 + 0.3 * hash21(vec2(id.y * 0.37, seed * 31.0))) * (1.0 - 0.28 * seam);
            albedo *= 0.85 + 0.3 * fbm(vec2(u * 0.5, fragPos.y * 0.08) + seed * 40.0);

            if (win > 0.5)
            {
                albedo = vec3(0.012, 0.016, 0.030);
                spec = 1.2;
                shin = 140.0;
                refl = 0.6;
                float mull = step(abs(f.x - 0.5), 0.012);
                emis = wc * lit * 1.5 * (1.0 - mull);
            }
            ao = mix(0.55, 1.0, smoothstep(0.0, 8.0, fragPos.y));
        }
        else if (n.y > 0.5)
        {
            albedo *= 0.45 * (0.7 + 0.6 * fbm(q * 1.1));
            spec = 0.03;
        }
    }
    else if (matId == 5)                  // car paint
    {
        spec = 1.0;
        shin = 110.0;
        refl = 0.42;
        ao = mix(0.6, 1.0, smoothstep(0.1, 0.7, fragPos.y));
    }
    else if (matId == 6)                  // glass
    {
        albedo *= 0.35;
        spec = 1.4;
        shin = 160.0;
        refl = 0.9;
    }
    else if (matId == 7)                  // rubber / dark plastic
    {
        albedo *= 0.6;
        spec = 0.04;
        shin = 6.0;
    }
    else if (matId == 8)                  // metal, chrome
    {
        spec = 1.3;
        shin = 70.0;
        refl = 0.75;
        reflTint = vec3(0.85, 0.90, 1.0);
    }
    else if (matId == 10)                 // far mountains
    {
        albedo *= 0.6 + 0.8 * fbm(q * 0.004 + fragPos.y * 0.01);
        spec = 0.0;
        fogCap = 0.80;
    }
    else if (matId == 11)                 // palm leaves
    {
        albedo *= 0.65 + 0.7 * vnoise(q * 3.0 + fragPos.y * 2.0);
        emis = albedo * 0.10;
        spec = 0.10;
        shin = 12.0;
    }

    // ---- lighting
    gN = n;
    gV = V;
    gShin = shin;

    vec3 moonCol = vec3(0.42, 0.52, 0.92);
    float ndl = max(dot(n, MOON), 0.0);
    gDiff = moonCol * 0.95 * ndl;
    gSpec = moonCol * pow(max(dot(n, normalize(MOON + V)), 0.0), shin) * step(0.001, ndl);

    streetLamps();
    for (int i = 0; i < 4; i++)
    {
        if (neonCol[i].r + neonCol[i].g + neonCol[i].b > 0.001) addPoint(neonPos[i], neonCol[i], 70.0);
    }

    if (carPos.w > 0.5)
    {
        vec3 d = fragPos - carPos.xyz;
        float cd = length(d);
        if (cd < 48.0 && cd > 0.01)
        {
            vec3 L = d / cd;
            float cone = smoothstep(0.80, 0.94, dot(L, carDir));
            float att = 1.0 - cd / 48.0;
            att *= att;
            float cn = clamp(dot(n, -L) * 0.7 + 0.3, 0.0, 1.0);
            gDiff += vec3(1.0, 0.93, 0.78) * 3.2 * cone * att * cn;
        }
    }

    vec3 amb = mix(vec3(0.055, 0.050, 0.055), vec3(0.075, 0.100, 0.210), n.y * 0.5 + 0.5) * ao;
    vec3 col = albedo * (amb + gDiff * mix(1.0, ao, 0.5));
    col += gSpec * spec;

    float fres = pow(1.0 - max(dot(n, V), 0.0), 4.0);
    vec3 R = reflect(-V, n);
    vec3 env = skyCol(R) + vec3(0.06, 0.035, 0.03) * smoothstep(0.35, 0.0, R.y);
    col += env * reflTint * refl * (0.12 + 0.88 * fres);
    col += emis;

    // ---- fog into the night haze
    float fogAmt = min(1.0 - exp(-pow(dist * 0.0052, 2.0)), fogCap);
    col = mix(col, HORIZON, fogAmt);
    finalColor = vec4(display(col), 1.0);
}
""";

    const string FlatFs = """
#version 330
in vec3 fragPos;
in vec3 fragNormal;
uniform vec4 colDiffuse;
uniform vec3 viewPos;
out vec4 finalColor;

const vec3 HORIZON = vec3(0.040, 0.025, 0.042);

void main()
{
    float dist = length(viewPos - fragPos);
    float fogAmt = 1.0 - exp(-pow(dist * 0.0052, 2.0));
    vec3 c = 1.0 - exp(-HORIZON * 2.0);
    vec3 fogCol = pow(c, vec3(1.0 / 2.2));
    finalColor = vec4(mix(colDiffuse.rgb, fogCol, fogAmt), colDiffuse.a);
}
""";

    const string SkyFs = """
#version 330
in vec3 fragPos;
in vec3 fragNormal;
uniform vec3 viewPos;
out vec4 finalColor;

const vec3 MOON = vec3(-0.618, 0.459, -0.638);
const vec3 HORIZON = vec3(0.040, 0.025, 0.042);

float hash13(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

void main()
{
    vec3 d = normalize(fragPos - viewPos);
    float h = clamp(d.y, 0.0, 1.0);
    vec3 c = mix(HORIZON, vec3(0.010, 0.014, 0.040), pow(h, 0.4));
    c = mix(c, vec3(0.002, 0.003, 0.012), smoothstep(0.3, 1.0, h));
    c += vec3(0.05, 0.028, 0.020) * exp(-abs(d.y) * 9.0);      // light pollution of the city

    vec3 sp = d * 190.0;
    vec3 cell = floor(sp);
    vec3 f = fract(sp) - 0.5;
    float s = hash13(cell);
    float star = step(0.9965, s) * smoothstep(0.32, 0.0, length(f));
    c += vec3(0.9, 0.95, 1.0) * star * 0.9 * smoothstep(0.04, 0.35, d.y);

    float md = dot(d, MOON);
    c += vec3(1.0, 0.96, 0.85) * 3.5 * smoothstep(0.9990, 0.9994, md);
    c += vec3(0.20, 0.27, 0.50) * pow(max(md, 0.0), 90.0) * 0.5;

    c = 1.0 - exp(-c * 2.0);
    finalColor = vec4(pow(c, vec3(1.0 / 2.2)), 1.0);
}
""";

    const string PostVs = """
#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;
uniform mat4 mvp;
out vec2 fragTexCoord;
out vec4 fragColor;
void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
""";

    const string PostFs = """
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec2 resolution;
uniform float time;
out vec4 finalColor;

float hash21(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// FXAA (Lottes, light version)
vec3 fxaa(vec2 uv, vec2 px)
{
    vec3 lumaW = vec3(0.299, 0.587, 0.114);
    vec3 rgbNW = texture(texture0, uv + vec2(-1.0, -1.0) * px).rgb;
    vec3 rgbNE = texture(texture0, uv + vec2(1.0, -1.0) * px).rgb;
    vec3 rgbSW = texture(texture0, uv + vec2(-1.0, 1.0) * px).rgb;
    vec3 rgbSE = texture(texture0, uv + vec2(1.0, 1.0) * px).rgb;
    vec3 rgbM = texture(texture0, uv).rgb;
    float lNW = dot(rgbNW, lumaW);
    float lNE = dot(rgbNE, lumaW);
    float lSW = dot(rgbSW, lumaW);
    float lSE = dot(rgbSE, lumaW);
    float lM = dot(rgbM, lumaW);
    float lMin = min(lM, min(min(lNW, lNE), min(lSW, lSE)));
    float lMax = max(lM, max(max(lNW, lNE), max(lSW, lSE)));

    vec2 dir;
    dir.x = -((lNW + lNE) - (lSW + lSE));
    dir.y = ((lNW + lSW) - (lNE + lSE));
    float dirReduce = max((lNW + lNE + lSW + lSE) * 0.03125, 0.0078125);
    float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
    dir = clamp(dir * rcpDirMin, vec2(-8.0), vec2(8.0)) * px;

    vec3 rgbA = 0.5 * (texture(texture0, uv + dir * (1.0 / 3.0 - 0.5)).rgb
                     + texture(texture0, uv + dir * (2.0 / 3.0 - 0.5)).rgb);
    vec3 rgbB = rgbA * 0.5 + 0.25 * (texture(texture0, uv + dir * -0.5).rgb
                                   + texture(texture0, uv + dir * 0.5).rgb);
    float lB = dot(rgbB, lumaW);
    return (lB < lMin || lB > lMax) ? rgbA : rgbB;
}

void main()
{
    vec2 uv = fragTexCoord;
    vec2 px = 1.0 / resolution;
    float sc = resolution.y / 1080.0;

    vec3 col = fxaa(uv, px);

    // bloom: two spiral rings of bright-pass samples (tight + wide glow)
    vec3 bloom = vec3(0.0);
    for (int i = 0; i < 16; i++)
    {
        float a = float(i) * 2.39996;
        float r = sqrt((float(i) + 0.5) / 16.0);
        vec2 o = vec2(cos(a), sin(a)) * r;
        vec3 s1 = texture(texture0, uv + o * px * 9.0 * sc).rgb;
        vec3 s2 = texture(texture0, uv + o * px * 32.0 * sc).rgb;
        bloom += max(s1 - 0.60, 0.0) * 1.0 + max(s2 - 0.52, 0.0) * 0.8;
    }
    col += bloom / 16.0 * 1.25;

    // gentle contrast curve
    col = mix(col, col * col * (3.0 - 2.0 * col), 0.22);

    // vignette
    vec2 vc = (uv - 0.5) * vec2(1.0, 0.9);
    col *= mix(0.60, 1.0, smoothstep(0.85, 0.25, length(vc)));

    // film grain
    col += (hash21(gl_FragCoord.xy + fract(time) * 173.0) - 0.5) * 0.022;

    finalColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
""";

    // ------------------------------------------------------------------------------------------ setup
    public static void Init()
    {
        Rlgl.SetClipPlanes(0.2, 3500.0);

        litSh = Raylib.LoadShaderFromMemory(Vs, Fs);
        flatSh = Raylib.LoadShaderFromMemory(Vs, FlatFs);
        skySh = Raylib.LoadShaderFromMemory(Vs, SkyFs);
        postSh = Raylib.LoadShaderFromMemory(PostVs, PostFs);

        lit = MakeModel(Raylib.GenMeshCube(1f, 1f, 1f), litSh);
        flat = MakeModel(Raylib.GenMeshCube(1f, 1f, 1f), flatSh);
        cyl = MakeModel(Raylib.GenMeshCylinder(0.5f, 1f, 24), litSh);
        cone = MakeModel(Raylib.GenMeshCone(0.5f, 1f, 7), litSh);
        sky = MakeModel(Raylib.GenMeshSphere(1f, 24, 48), skySh);

        viewLoc = Raylib.GetShaderLocation(litSh, "viewPos");
        matLoc = Raylib.GetShaderLocation(litSh, "matId");
        seedLoc = Raylib.GetShaderLocation(litSh, "seed");
        timeLoc = Raylib.GetShaderLocation(litSh, "time");
        neonPosLoc = Raylib.GetShaderLocation(litSh, "neonPos");
        neonColLoc = Raylib.GetShaderLocation(litSh, "neonCol");
        carPosLoc = Raylib.GetShaderLocation(litSh, "carPos");
        carDirLoc = Raylib.GetShaderLocation(litSh, "carDir");
        flatViewLoc = Raylib.GetShaderLocation(flatSh, "viewPos");
        skyViewLoc = Raylib.GetShaderLocation(skySh, "viewPos");
        postResLoc = Raylib.GetShaderLocation(postSh, "resolution");
        postTimeLoc = Raylib.GetShaderLocation(postSh, "time");

        SetNeonOff();
        SetCarLight(false, Vector3.Zero, Vector3.UnitZ);
    }

    static Model MakeModel(Mesh mesh, Shader sh)
    {
        Model m = Raylib.LoadModelFromMesh(mesh);
        m.Materials[0].Shader = sh;
        return m;
    }

    // ------------------------------------------------------------------------------------------ per frame uniforms
    public static void SetView(Vector3 pos, float time)
    {
        float* p = stackalloc float[3] { pos.X, pos.Y, pos.Z };
        if (viewLoc >= 0) Raylib.SetShaderValue(litSh, viewLoc, p, ShaderUniformDataType.Vec3);
        if (flatViewLoc >= 0) Raylib.SetShaderValue(flatSh, flatViewLoc, p, ShaderUniformDataType.Vec3);
        if (skyViewLoc >= 0) Raylib.SetShaderValue(skySh, skyViewLoc, p, ShaderUniformDataType.Vec3);
        float t = time;
        if (timeLoc >= 0) Raylib.SetShaderValue(litSh, timeLoc, &t, ShaderUniformDataType.Float);
    }

    static float Lin(byte v) => MathF.Pow(v / 255f, 2.2f);

    static void SetNeonOff()
    {
        float* z = stackalloc float[12];
        for (int i = 0; i < 12; i++) z[i] = 0f;
        if (neonPosLoc >= 0) Raylib.SetShaderValueV(litSh, neonPosLoc, z, ShaderUniformDataType.Vec3, 4);
        if (neonColLoc >= 0) Raylib.SetShaderValueV(litSh, neonColLoc, z, ShaderUniformDataType.Vec3, 4);
    }

    /// <summary>The 4 casinos nearest to the camera light up their street with coloured neon.</summary>
    public static void SetNeon(List<Building> list, Vector3 cam, float time)
    {
        float* pos = stackalloc float[12];
        float* col = stackalloc float[12];
        for (int i = 0; i < 12; i++) { pos[i] = 0f; col[i] = 0f; }

        for (int k = 0; k < 4; k++) { nIdx[k] = -1; nD[k] = float.MaxValue; }
        for (int i = 0; i < list.Count; i++)
        {
            Vector3 c = list[i].Center;
            float dx = c.X - cam.X, dz = c.Z - cam.Z;
            float d = dx * dx + dz * dz;
            for (int k = 0; k < 4; k++)
            {
                if (d < nD[k])
                {
                    for (int m = 3; m > k; m--) { nD[m] = nD[m - 1]; nIdx[m] = nIdx[m - 1]; }
                    nD[k] = d; nIdx[k] = i;
                    break;
                }
            }
        }

        for (int k = 0; k < 4; k++)
        {
            if (nIdx[k] < 0 || nD[k] > 260f * 260f) continue;
            Building b = list[nIdx[k]];
            float pulse = 0.9f + 0.1f * MathF.Sin(time * 3f + nIdx[k]);
            pos[k * 3 + 0] = b.Center.X;
            pos[k * 3 + 1] = 9f;
            pos[k * 3 + 2] = b.MinZ - 5f;
            col[k * 3 + 0] = Lin(b.Neon.R) * 2.4f * pulse;
            col[k * 3 + 1] = Lin(b.Neon.G) * 2.4f * pulse;
            col[k * 3 + 2] = Lin(b.Neon.B) * 2.4f * pulse;
        }

        if (neonPosLoc >= 0) Raylib.SetShaderValueV(litSh, neonPosLoc, pos, ShaderUniformDataType.Vec3, 4);
        if (neonColLoc >= 0) Raylib.SetShaderValueV(litSh, neonColLoc, col, ShaderUniformDataType.Vec3, 4);
    }

    /// <summary>Headlights of the car the player drives.</summary>
    public static void SetCarLight(bool on, Vector3 pos, Vector3 dir)
    {
        float* a = stackalloc float[4] { pos.X, pos.Y, pos.Z, on ? 1f : 0f };
        float* b = stackalloc float[3] { dir.X, dir.Y, dir.Z };
        if (carPosLoc >= 0) Raylib.SetShaderValue(litSh, carPosLoc, a, ShaderUniformDataType.Vec4);
        if (carDirLoc >= 0) Raylib.SetShaderValue(litSh, carDirLoc, b, ShaderUniformDataType.Vec3);
    }

    // ------------------------------------------------------------------------------------------ user models
    public static int Count(int cat) => custom[cat].Count;

    /// <summary>Is there a user car model of this CarKind?</summary>
    public static bool HasCarKind(int kind)
    {
        foreach (var c in custom[CatCar]) if (c.Kind == kind) return true;
        return false;
    }

    /// <summary>CarKind (as int) of the model at the given index in the car list. Used by Car.Roll
    /// so that the CarType follows the file name of whichever model was picked.</summary>
    public static int CarModelKind(int index)
    {
        List<Custom> l = custom[CatCar];
        if (index < 0 || index >= l.Count) return (int)CarKind.Sedan;
        return l[index].Kind;
    }

    /// <summary>Random user car model of this CarKind (index into the car list), -1 if there is none.</summary>
    public static int RandomCarModel(int kind)
    {
        List<Custom> l = custom[CatCar];
        int n = 0;
        for (int i = 0; i < l.Count; i++) if (l[i].Kind == kind) n++;
        if (n == 0) return -1;
        int pick = U.R.Next(n);
        for (int i = 0; i < l.Count; i++)
            if (l[i].Kind == kind && pick-- == 0) return i;
        return -1;
    }

    /// <summary>Loads new model files from the "models" folder (next to the exe and in the working dir).</summary>
    public static string ScanModels()
    {
        string main = Path.Combine(AppContext.BaseDirectory, "models");
        string[] dirs = { main, Path.Combine(Directory.GetCurrentDirectory(), "models") };
        int added = 0, failed = 0;

        foreach (string dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "README.txt"),
                        "Put 3D models here (.glb .gltf .obj .iqm .vox .m3d) and press F6 in the game.\n" +
                        "File name decides what the model replaces:\n" +
                        "  car*.glb     - cars (several files = random variety). The class comes from the name:\n" +
                        "                 car_sedan_1  car_sport_2  car_taxi  car_suv  car_pickup  car_van  car_truck  car_bus\n" +
                        "                 (no keyword = sedan). Every class has its own size, speed, price and traffic share.\n" +
                        "  player*.glb  - your character\n" +
                        "  npc*.glb     - pedestrians (several files = random people)\n" +
                        "People walk by themselves: animations stored inside the .glb are used (names like idle / walk / run).\n" +
                        "Several files: npc_bob.glb + npc_bob@walk.glb + npc_bob@run.glb + npc_bob@idle.glb (same skeleton, e.g. Mixamo).\n" +
                        "A model without any animation gets a simple procedural walk (bob, sway, lean).\n" +
                        "  cop_car*.glb - police cruisers (chase you when wanted). cop_police_1.glb, cop_car_2.glb ...\n" +
                        "  cop_npc*.glb - police officers on foot (cop_officer_1.glb ...).\n" +
                        "Without these files the game uses built-in police car and officer bodies.\n" +
                        "Size is fitted automatically. If a model faces the wrong way press F8 (rotates 90 degrees).\n");
                }
                string[] files = Directory.GetFiles(dir);
                Array.Sort(files);
                foreach (string f in files)
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    string ext = Path.GetExtension(name);
                    if (Array.IndexOf(Exts, ext) < 0) continue;
                    if (name.Contains('@')) continue;                  // "npc_bob@walk.glb": extra clip for npc_bob, handled below
                    int cat = -1;
                    // The longest prefix wins: cop_car* must beat car*, cop_npc* must beat npc*.
                    for (int c = 0; c < CatNames.Length; c++)
                        if (name.StartsWith(CatNames[c])) { cat = CatIds[c]; break; }
                    if (cat < 0) continue;
                    string full = Path.GetFullPath(f);
                    if (!loadedPaths.Add(full)) continue;
                    if (LoadCustom(cat, full)) added++; else failed++;
                }
            }
            catch (Exception) { failed++; }
        }

        // extra animation files:  <model>@<clip>.glb  (e.g. npc_bob@walk.glb, player@run.glb) - the Unity/Mixamo style
        foreach (string dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir);
                Array.Sort(files);
                foreach (string f in files)
                {
                    string nm = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                    int at = nm.IndexOf('@');
                    if (at <= 0 || at == nm.Length - 1) continue;
                    string full = Path.GetFullPath(f);
                    if (!loadedPaths.Add(full)) continue;
                    if (AddClipFile(Path.Combine(Path.GetDirectoryName(full), nm[..at]), nm[(at + 1)..], full)) added++; else failed++;
                }
            }
            catch (Exception) { failed++; }
        }

        return $"Models: car {custom[0].Count}, player {custom[1].Count}, npc {custom[2].Count}, cop_car {custom[3].Count}, cop_npc {custom[4].Count}"
             + (added > 0 ? $" (+{added})" : "") + (failed > 0 ? $", failed {failed}" : "")
             + "  folder: " + main;
    }

    static bool LoadCustom(int cat, string path)
    {
        try
        {
            Model m = Raylib.LoadModel(path);
            if (m.MeshCount == 0) return false;
            for (int i = 0; i < m.MaterialCount; i++) m.Materials[i].Shader = litSh;

            // optional animation sheet "<name>.anim": the model file holds N baked frames, 'mpf' meshes each
            Model[] frames = null;
            Dictionary<string, (int Start, int Count)> clips = null;
            string animPath = Path.ChangeExtension(path, ".anim");
            if (File.Exists(animPath))
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                int mpf = 0;
                var cl = new Dictionary<string, (int Start, int Count)>();
                Vector3 gun = Vector3.Zero;
                bool hasGun = false;
                foreach (string line in File.ReadAllLines(animPath))
                {
                    string[] t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length < 2) continue;
                    if (t[0] == "mpf") mpf = int.Parse(t[1], inv);
                    else if (t[0] == "gun" && t.Length >= 4)
                    {
                        gun = new Vector3(float.Parse(t[1], inv), float.Parse(t[2], inv), float.Parse(t[3], inv));
                        hasGun = true;
                    }
                    else if (t.Length >= 3) cl[t[0]] = (int.Parse(t[1], inv), int.Parse(t[2], inv));
                }
                if (mpf > 0 && m.MeshCount % mpf == 0)
                {
                    int nf = m.MeshCount / mpf;
                    frames = new Model[nf];
                    for (int f = 0; f < nf; f++)
                    {
                        Model fm = m;                                   // a view: same materials, a slice of the meshes
                        fm.MeshCount = mpf;
                        fm.Meshes = m.Meshes + f * mpf;
                        fm.MeshMaterial = m.MeshMaterial + f * mpf;
                        frames[f] = fm;
                    }
                    clips = cl;
                    if (hasGun && cat == CatPlayer) GunShift = gun - new Vector3(0.30f, 1.50f, 0.70f);
                }
            }

            BoundingBox bb = Raylib.GetModelBoundingBox(frames != null ? frames[0] : m);
            Vector3 size = bb.Max - bb.Min;
            if (size.X < 1e-4f && size.Y < 1e-4f && size.Z < 1e-4f) return false;

            var c = new Custom { Mdl = m, Frames = frames, Clips = clips };
            if (cat == CatCar || cat == CatCopCar)
            {
                c.Kind = (cat == CatCopCar) ? (int)CarKind.Police : (int)CarType.Parse(Path.GetFileNameWithoutExtension(path));
                c.BaseYaw = size.X > size.Z ? 90f : 0f;                      // long axis -> forward
                CarKind ck = (cat == CatCopCar) ? CarKind.Police : (CarKind)c.Kind;
                c.Scale = CarType.Of(ck).Len / MathF.Max(MathF.Max(size.X, size.Z), 0.001f);   // length of the class
            }
            else
            {
                // Some character exports (Sketchfab/Mixamo, e.g. cop_npc.glb) come in lying along X instead of
                // standing along Y: the officer then lay on the road as a small flat figure. Stand it up:
                // swap X<->Y and flip Z (a proper 180 deg rotation about the (1,1,0) axis): head +X -> +Y, face -Z -> +Z.
                if (size.X > size.Y * 1.3f && size.X > size.Z * 1.3f)
                {
                    c.Tilt = new Quaternion(0.70710678f, 0.70710678f, 0f, 0f);
                    bb = new BoundingBox(new Vector3(bb.Min.Y, bb.Min.X, -bb.Max.Z),
                                         new Vector3(bb.Max.Y, bb.Max.X, -bb.Min.Z));
                    size = bb.Max - bb.Min;
                }
                // player, npc, cop_npc: fit within 1.8 m tall AND 0.8 m wide so a model that is modelled
                // short-and-wide does not balloon into a fat blob when we scale it up to full height.
                float sH = 1.8f / MathF.Max(size.Y, 0.001f);
                float sW = 0.8f / MathF.Max(MathF.Max(size.X, size.Z), 0.001f);
                c.Scale = MathF.Min(sH, sW);
            }
            Vector3 ctr = (bb.Min + bb.Max) * 0.5f;
            c.Off = new Vector3(-ctr.X, -bb.Min.Y, -ctr.Z) * c.Scale;
            c.BasePath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path)).ToLowerInvariant();

            // people without a baked .anim sheet: use the skeleton animations stored in the model file itself
            if (frames == null && cat != CatCar && cat != CatCopCar)
            {
                LoadSkeletal(c, path, null);
                // weapon in hand for a model that has no baked 'aim' pose: lower it a bit so it sits at chest height
                if (cat == CatPlayer && GunShift == Vector3.Zero && !HasSkel(c, "aim"))
                    GunShift = new Vector3(-0.10f, -0.18f, 0f);
            }
            custom[cat].Add(c);
            return true;
        }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------------------------------ skeletal animation
    static bool HasSkel(Custom c, string k) => c.Skel != null && c.Skel.ContainsKey(k);

    static bool Has(HashSet<string> t, params string[] w) { foreach (string x in w) if (t.Contains(x)) return true; return false; }

    /// <summary>Animation name from the file ("Walking", "Armature|Run", "idle_2" ...) -> canonical clip name, null = unknown.</summary>
    static string Classify(string raw)
    {
        var t = new HashSet<string>(raw.ToLowerInvariant().Split(new[] { '|', '_', '-', ' ', '.', '@', ':', '/', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' },
                                    StringSplitOptions.RemoveEmptyEntries));
        bool aim = Has(t, "aim", "aiming", "shoot", "shooting", "fire", "firing", "rifle", "pistol", "gun");
        if (Has(t, "dead", "death", "dying", "die", "died", "dies")) return "dead";
        if (Has(t, "sprint", "sprinting")) return aim ? "aimrun" : "sprint";
        if (Has(t, "run", "running", "jog", "jogging")) return aim ? "aimrun" : "run";
        if (Has(t, "walk", "walking", "stroll")) return aim ? "aimrun" : "walk";
        if (Has(t, "jump", "jumping", "fall", "falling", "air", "airborne")) return aim ? "airaim" : "air";
        if (aim) return "aim";
        if (Has(t, "idle", "stand", "standing", "breathing", "rest", "wait", "waiting")) return "idle";
        return null;
    }

    // which clip stands in for a missing one (first existing wins)
    static readonly Dictionary<string, string[]> SkelFallback = new()
    {
        ["idle"] = new[] { "idle", "walk", "run", "aim" },
        ["walk"] = new[] { "walk", "run", "sprint", "idle" },
        ["run"] = new[] { "run", "sprint", "walk" },
        ["sprint"] = new[] { "sprint", "run", "walk" },
        ["aim"] = new[] { "aim", "idle", "walk" },
        ["aimrun"] = new[] { "aimrun", "run", "walk", "aim", "idle" },
        ["air"] = new[] { "air", "idle", "walk" },
        ["airaim"] = new[] { "airaim", "air", "aim", "idle" },
        ["dead"] = new[] { "dead" },
    };

    /// <summary>Reads all animations of 'file' that fit the skeleton of c. nameOverride = clip name taken from "model@clip.glb".</summary>
    static unsafe int ReadAnims(Custom c, string file, string nameOverride)
    {
        if (c.Mdl.BoneCount <= 0) return 0;
        int n = 0;
        ModelAnimation* a = Raylib.LoadModelAnimations(file, ref n);
        if (a == null || n <= 0) return 0;
        c.Anims ??= new List<ModelAnimation>();
        c.Skel ??= new Dictionary<string, int>();
        int added = 0;
        for (int i = 0; i < n; i++)
        {
            ModelAnimation an = a[i];
            if (an.FrameCount <= 0 || an.BoneCount != c.Mdl.BoneCount) continue;
            string raw = nameOverride ?? new string(a[i].Name);
            string key = Classify(raw);
            if (key == null && nameOverride != null) key = nameOverride.ToLowerInvariant();
            if (key == null && n == 1) key = "walk";                  // a lone unnamed clip is the walk cycle
            if (key == null || c.Skel.ContainsKey(key)) continue;
            c.Anims.Add(an);
            c.Skel[key] = c.Anims.Count - 1;
            added++;
        }
        return added;
    }

    static void LoadSkeletal(Custom c, string path, string nameOverride)
    {
        try
        {
            if (ReadAnims(c, path, nameOverride) > 0 && !SkinSane(c)) { c.Skel = null; c.Anims = null; }
        }
        catch (Exception) { c.Skel = null; c.Anims = null; }
    }

    /// <summary>"npc_bob@walk.glb" adds the clip "walk" to the model npc_bob.</summary>
    static bool AddClipFile(string baseNoExt, string clip, string file)
    {
        baseNoExt = baseNoExt.ToLowerInvariant();
        foreach (var l in custom)
            foreach (Custom c in l)
                if (c.BasePath == baseNoExt)
                {
                    try { return ReadAnims(c, file, clip) > 0; }
                    catch (Exception) { return false; }
                }
        return false;
    }

    /// <summary>Skin one frame and check that the result is still a person-sized body (guards against broken unit scales).
    /// If it is not, the pose is thrown away and the model falls back to the procedural walk.</summary>
    static unsafe bool SkinSane(Custom c)
    {
        if (c.Skel == null || c.Skel.Count == 0) return false;
        Raylib.UpdateModelAnimation(c.Mdl, c.Anims[0], 0);
        float bind = 0f, anim = 0f;
        bool ok = true;
        for (int mi = 0; mi < c.Mdl.MeshCount; mi++)
        {
            Mesh m = c.Mdl.Meshes[mi];
            if (m.AnimVertices == null || m.Vertices == null || m.VertexCount <= 0) { ok = false; break; }
            Extent(m.Vertices, m.VertexCount, ref bind);
            Extent(m.AnimVertices, m.VertexCount, ref anim);
        }
        if (ok) { float r = anim / MathF.Max(bind, 1e-5f); ok = bind > 0f && !float.IsNaN(r) && r > 0.5f && r < 2.0f; }
        if (!ok) RestoreBindPose(c);
        c.LastAnim = -1; c.LastFrame = -1;
        return ok;
    }

    static unsafe void Extent(float* v, int count, ref float best)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            float x = v[i * 3], y = v[i * 3 + 1], z = v[i * 3 + 2];
            if (x < x0) x0 = x; if (x > x1) x1 = x;
            if (y < y0) y0 = y; if (y > y1) y1 = y;
            if (z < z0) z0 = z; if (z > z1) z1 = z;
        }
        best = MathF.Max(best, MathF.Max(x1 - x0, MathF.Max(y1 - y0, z1 - z0)));
    }

    static unsafe void RestoreBindPose(Custom c)
    {
        for (int mi = 0; mi < c.Mdl.MeshCount; mi++)
        {
            Mesh m = c.Mdl.Meshes[mi];
            if (m.Vertices != null) Raylib.UpdateMeshBuffer(m, 0, m.Vertices, m.VertexCount * 3 * sizeof(float), 0);
            if (m.Normals != null) Raylib.UpdateMeshBuffer(m, 2, m.Normals, m.VertexCount * 3 * sizeof(float), 0);
        }
    }

    /// <summary>Skeletal clip lookup. Returns the animation index and the canonical name that was found, or -1.</summary>
    static int SkelFind(Custom c, string clip, out string found)
    {
        found = null;
        if (c.Skel == null) return -1;
        if (!SkelFallback.TryGetValue(clip, out string[] chain)) chain = new[] { clip };
        foreach (string k in chain)
            if (c.Skel.TryGetValue(k, out int ai)) { found = k; return ai; }
        return -1;
    }

    /// <summary>Procedural walk for models without any animation: body bob, sway, twist and a forward lean around the feet.</summary>
    static float WalkAmp(string clip) => clip switch
    {
        "walk" => 0.8f, "run" => 1.1f, "sprint" => 1.4f, "aimrun" => 0.9f, _ => 0f
    };

    /// <summary>F8: turns the models of a category by 90 degrees.</summary>
    public static string RotateFix(int cat)
    {
        yawFix[cat] = (yawFix[cat] + 90f) % 360f;
        return $"Model rotation ({CatDisplay[cat]}): {yawFix[cat]:0} deg";
    }

    public static bool DrawCustom(int cat, int variant, Vector3 pos, Quaternion q, Color tint)
        => DrawAnim(cat, variant, "idle", 0f, pos, q, tint);

    public static bool HasClip(int cat, int variant, string clip)
    {
        List<Custom> l = custom[cat];
        if (l.Count == 0) return false;
        Custom c = l[variant % l.Count];
        return (c.Clips != null && c.Clips.ContainsKey(clip)) || (c.Skel != null && c.Skel.ContainsKey(clip));
    }

    // if a model lacks a clip, fall back to a similar one
    static string NextClip(string k) => k switch
    {
        "sprint" => "run", "run" => "walk", "walk" => "idle",
        "aimrun" => "aim", "aim" => "idle",
        "airaim" => "air", "air" => "idle",
        _ => null
    };

    /// <param name="phase">position inside the clip, 0..1 (wraps); one full phase = one full stride</param>
    /// <param name="camDist">distance to the camera: far skeletal bodies are not re-skinned (saves CPU)</param>
    public static bool DrawAnim(int cat, int variant, string clip, float phase, Vector3 pos, Quaternion q, Color tint, float camDist = 0f)
    {
        List<Custom> l = custom[cat];
        if (l.Count == 0) return false;
        Custom c = l[variant % l.Count];
        Model mdl = c.Mdl;
        Quaternion extra = Quaternion.Identity;                  // procedural walk, applied around the feet
        float bob = 0f;
        float ph = phase - MathF.Floor(phase);

        if (c.Frames != null && c.Clips != null)
        {
            (int Start, int Count) cl = (0, 1);
            string k = clip;
            while (k != null && !c.Clips.TryGetValue(k, out cl)) k = NextClip(k);
            if (k == null) cl = (0, 1);
            int fi = cl.Start + (cl.Count > 1 ? Math.Min(cl.Count - 1, (int)(ph * cl.Count)) : 0);
            mdl = c.Frames[Math.Clamp(fi, 0, c.Frames.Length - 1)];
        }
        else
        {
            int ai = SkelFind(c, clip, out string found);
            if (ai >= 0)
            {
                ModelAnimation an = c.Anims[ai];
                bool moving = clip is "walk" or "run" or "sprint" or "aimrun";
                float t;
                if (found == clip && !moving) t = (float)(Raylib.GetTime() * 0.35 + pos.X * 0.37 + pos.Z * 0.11);   // idle / aim: slow breathing loop
                else if (moving && found is "walk" or "run" or "sprint" or "aimrun") t = ph;                        // stride phase drives the cycle
                else t = 0f;                                                                                          // stand-in clip: hold its first pose
                t -= MathF.Floor(t);
                int frame = Math.Min(an.FrameCount - 1, (int)(t * an.FrameCount));
                if (camDist < 70f || c.LastAnim < 0)
                {
                    if (c.LastAnim != ai || c.LastFrame != frame)      // the mesh is shared, so this always reflects its current pose
                    {
                        Raylib.UpdateModelAnimation(c.Mdl, an, frame);
                        c.LastAnim = ai; c.LastFrame = frame;
                    }
                }
            }
            else
            {
                float amp = WalkAmp(clip);
                if (amp > 0f)
                {
                    float s = MathF.Sin(ph * MathF.Tau);
                    bob = MathF.Abs(s) * 0.05f * amp;
                    extra = Quaternion.CreateFromYawPitchRoll(s * 0.10f * amp, 0.05f + 0.05f * amp, s * 0.07f * amp);
                }
            }
        }

        Quaternion qb = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (c.BaseYaw + yawFix[cat]) * (MathF.PI / 180f));
        Quaternion qy = q * extra * qb;
        Quaternion qf = qy * c.Tilt;                       // tilt (stand upright) first, then yaw, then the entity rotation
        Vector3 o = Vector3.Transform(c.Off, qy);          // Off is already measured in the upright frame
        o.Y += bob;
        AxisAngle(qf, out Vector3 axis, out float deg);
        Use(M.Generic, 0f);
        Raylib.DrawModelEx(mdl, pos + o, axis, deg, new Vector3(c.Scale, c.Scale, c.Scale), tint);
        return true;
    }

    // ------------------------------------------------------------------------------------------ materials
    static void Use(int mat, float seed)
    {
        if (mat != curMat)
        {
            int m = mat;
            if (matLoc >= 0) Raylib.SetShaderValue(litSh, matLoc, &m, ShaderUniformDataType.Int);
            curMat = mat;
        }
        if (seed != curSeed)
        {
            float s = seed;
            if (seedLoc >= 0) Raylib.SetShaderValue(litSh, seedLoc, &s, ShaderUniformDataType.Float);
            curSeed = seed;
        }
    }

    static void AxisAngle(Quaternion q, out Vector3 axis, out float deg)
    {
        q = Quaternion.Normalize(q);
        float w = Math.Clamp(q.W, -1f, 1f);
        float sn = MathF.Sqrt(MathF.Max(0f, 1f - w * w));
        if (sn < 1e-4f) { axis = Vector3.UnitY; deg = 0f; return; }
        axis = new Vector3(q.X / sn, q.Y / sn, q.Z / sn);
        deg = 2f * MathF.Acos(w) * U.Deg;
    }

    // ------------------------------------------------------------------------------------------ drawing primitives
    /// <summary>Lit box rotated around Y.</summary>
    public static void Box(Vector3 center, Vector3 size, float yawRad, Color c, int mat = 0, float seed = 0f)
    {
        Use(mat, seed);
        Raylib.DrawModelEx(lit, center, Vector3.UnitY, yawRad * U.Deg, size, c);
    }

    /// <summary>Lit box with a full rotation.</summary>
    public static void BoxQ(Vector3 center, Vector3 size, Quaternion q, Color c, int mat = 0, float seed = 0f)
    {
        Use(mat, seed);
        AxisAngle(q, out Vector3 axis, out float deg);
        Raylib.DrawModelEx(lit, center, axis, deg, size, c);
    }

    /// <summary>Lit cylinder, centred on 'center'; its axis is local Y of the rotation q.</summary>
    public static void CylQ(Vector3 center, float radius, float length, Quaternion q, Color c, int mat = 0)
    {
        Use(mat, 0f);
        Vector3 up = Vector3.Transform(Vector3.UnitY, q);
        Vector3 pos = center - up * (length * 0.5f);
        AxisAngle(q, out Vector3 axis, out float deg);
        Raylib.DrawModelEx(cyl, pos, axis, deg, new Vector3(radius * 2f, length, radius * 2f), c);
    }

    /// <summary>Cone standing on the ground (far mountains).</summary>
    public static void Mountain(Vector3 basePos, float radius, float height, float yawRad, Color c)
    {
        Use(M.Mountain, 0f);
        Raylib.DrawModelEx(cone, basePos, Vector3.UnitY, yawRad * U.Deg, new Vector3(radius * 2f, height, radius * 2f), c);
    }

    /// <summary>Unlit (emissive) box, only fogged.</summary>
    public static void Flat(Vector3 center, Vector3 size, float yawRad, Color c) =>
        Raylib.DrawModelEx(flat, center, Vector3.UnitY, yawRad * U.Deg, size, c);

    public static void FlatQ(Vector3 center, Vector3 size, Quaternion q, Color c)
    {
        AxisAngle(q, out Vector3 axis, out float deg);
        Raylib.DrawModelEx(flat, center, axis, deg, size, c);
    }

    /// <summary>Soft blob shadow under cars and people.</summary>
    public static void Shadow(Vector3 p, float yaw, float w, float l, float k = 1f)
    {
        float gy = World.GroundY(p.X, p.Z) + 0.03f;
        Flat(new Vector3(p.X, gy, p.Z), new Vector3(w + 0.9f, 0.01f, l + 0.9f), yaw, U.C(0, 0, 0, (int)(26 * k)));
        Flat(new Vector3(p.X, gy + 0.004f, p.Z), new Vector3(w + 0.45f, 0.01f, l + 0.45f), yaw, U.C(0, 0, 0, (int)(34 * k)));
        Flat(new Vector3(p.X, gy + 0.008f, p.Z), new Vector3(w, 0.01f, l), yaw, U.C(0, 0, 0, (int)(48 * k)));
    }

    public static void DrawSky(Vector3 cam)
    {
        // negative X scale flips the winding, so the inside of the sphere is the front face
        Raylib.DrawModelEx(sky, cam, Vector3.UnitY, 0f, new Vector3(-2800f, 2800f, 2800f), Color.White);
    }

    /// <summary>Semi-transparent things (light beams) must not write depth.</summary>
    public static void BeginTranslucent() => Rlgl.DisableDepthMask();
    public static void EndTranslucent() => Rlgl.EnableDepthMask();

    // ------------------------------------------------------------------------------------------ scene render target + post
    public static void BeginScene(int w, int h)
    {
        if (!rtOk || w != rtW || h != rtH)
        {
            if (rtOk) Raylib.UnloadRenderTexture(rt);
            rt = Raylib.LoadRenderTexture(w, h);
            Raylib.SetTextureFilter(rt.Texture, TextureFilter.Bilinear);
            rtW = w; rtH = h; rtOk = true;
        }
        curMat = -1;
        curSeed = float.NaN;
        Raylib.BeginTextureMode(rt);
    }

    /// <summary>Ends the 3D pass, opens the real frame and draws the post-processed scene into it.</summary>
    public static void EndScene(float time)
    {
        Raylib.EndTextureMode();
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);

        float* res = stackalloc float[2] { rtW, rtH };
        float t = time;
        if (postResLoc >= 0) Raylib.SetShaderValue(postSh, postResLoc, res, ShaderUniformDataType.Vec2);
        if (postTimeLoc >= 0) Raylib.SetShaderValue(postSh, postTimeLoc, &t, ShaderUniformDataType.Float);

        Raylib.BeginShaderMode(postSh);
        Raylib.DrawTextureRec(rt.Texture, new Rectangle(0, 0, rtW, -rtH), Vector2.Zero, Color.White);
        Raylib.EndShaderMode();
    }
}
