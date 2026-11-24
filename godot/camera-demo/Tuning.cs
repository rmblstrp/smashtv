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

    // Room to room. Rooms sit on a grid; leaving one scrolls the screen to the next.
    public static readonly float ScrollSpeed = Mps(3f);          // SCRLXSPD/SCRLYSPD [3,0], WAVES.ASM:175 -> 9.65 m/s, linear
    public const float RoomStepX = 472f / PxPerMeter;            // CRCT1YX x step 0x1D8 px -> 27.8 m
    public const float RoomStepZ = 333f / PxPerMeter;            // CRCT1YX y step 0x152-5 px -> 19.6 m
    public const float WalkOutSide = 0xDF / SourceFps;           // RGTDOOR/LFTDOOR script, ROBOATT.ASM:2222 -> 4.08 s
    public const float WalkOutDown = 0xA3 / SourceFps;           // BOTDOOR script -> 2.98 s
    public const float WalkOutUp = 0xAA / SourceFps;             // TOPDOOR script -> 3.11 s
    public const float RoomFreeze = 120f / SourceFps;            // enemies frozen at room start, ROBO.ASM:1295
    public const float BannerAfterScroll = 1.5f;                 // measured in arcade footage, both transitions
    public const float BannerColorStep = 0.1f;                   // footage: a new colour at least every 0.2 s
    public const int RoomKillQuota = 15;                         // demo only: the arcade ends a room when its primary enemy runs out

    public const float GruntSpawnInterval = 18f / SourceFps;     // circuit 1 room 1 RATE 6 x 3 frames
    public const int MaxGrunts = 22;
    public const float GangChance = 0.14f;                       // RANDPER 140
}
