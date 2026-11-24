namespace CameraDemo;

/// <summary>
/// Numbers from docs/gameplay-reference.md, converted from the arcade's pixels-per-frame at
/// 54.71 Hz into metres per second. Scale: the player sprite is ~30 px tall, so 1 m = 17 px.
/// </summary>
public static class Tuning
{
    public const float PxPerMeter = 17f;
    public const float SourceFps = 54.71f;

    public static float Mps(float pxPerFrame) => pxPerFrame * SourceFps / PxPerMeter;

    // Arena: the player's legs roam X 37..354, Y 44..239 of a 400 x 256 screen -> ~18.6 x 11.5 m.
    public const float ArenaWidth = 20f;   // interior, X
    public const float ArenaDepth = 12f;   // interior, Z (north wall at -Z)
    public const float WallHeight = 3f;
    public const float WallThickness = 1f;
    public const float DoorWidth = 3.5f;

    public static readonly float PlayerSpeed = Mps(1.375f);      // ROBO.ASM:137  -> 4.4 m/s
    public const float PlayerRadius = 0.35f;
    public static readonly float BulletSpeed = Mps(7.125f);      // SHOTS.ASM:67  -> 22.9 m/s
    public const float FireInterval = 8f / SourceFps;            // 8-frame shot cycle -> 6.8 shots/s

    public static float GruntSpeed(int level) => Mps(0.5f + 0.125f * level);  // HULKS.ASM:733-753
    public const int GruntMaxLevel = 10;
    public const float GruntRadius = 0.35f;

    public static readonly float FatSpeed = Mps(0.5f);           // FATMAN.ASM
    public const int FatHits = 8;                                // HITFAT[3], factory difficulty
    public const float FatRadius = 0.75f;

    public static readonly float TankSpeed = Mps(1.125f);        // TANK.ASM:108 -> 3.6 m/s
    public const int TankTurretDamage = 17;                      // HITTNKS[3]; laser does 2 per hit
    public static readonly float CannonballSpeed = Mps(1.6f);    // TANK.ASM:1534 -> 5.1 m/s
    public const float TankRadius = 1.0f;

    public const float GruntSpawnInterval = 18f / SourceFps;     // circuit 1 room 1 RATE 6 x 3 frames
    public const int MaxGrunts = 22;
    public const float GangChance = 0.14f;                       // RANDPER 140
}
