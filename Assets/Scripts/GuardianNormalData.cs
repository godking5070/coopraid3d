using System;

namespace RaidRogue
{
    public enum GuardianPattern { None, Punishment, Sweep, FallingStone, PlazaSlam, GateCharge, Resonance, Overload, ShieldPush, LineFracture, Lockdown, Transformation, TwistedExecution, FullLockdown, AerialCharge }
    public enum GuardianStep { Rest, Telegraph, Locked, CounterWindow, Dash, StaggerCheck, Groggy, Stopped, Escape, Recovery }

    // Initial tuning, not additional lore or a hard enrage. Inspector values belong to BossController.
    [Serializable]
    public sealed class GuardianNormalSettings
    {
        public float health = 30000, recoverySeconds = 5, autoDamage = 120;
        // Unity preserves serializable settings through script reload and Play-mode restoration.
        // Migrate only the original shipped default; preserve all other Inspector tuning.
        public int tuningRevision;
        public void MigrateInitialTuning()
        {
            if (tuningRevision >= 1) return;
            if (health == 65000f) health = 30000f;
            tuningRevision = 1;
        }
        public void EnsureTempoDefaults()
        {
            // Newly added fields may deserialize as zero in an older authored scene instance.
            if (tempo <= 0) tempo = 1.2f;
            if (initialRest <= 0) initialRest = 2;
            if (recoverySecondsHeavy <= 0) recoverySecondsHeavy = 5.5f;
            if (punishmentPrepare <= 0) punishmentPrepare = 3;
            if (sweepPrepare <= 0) sweepPrepare = 4;
            if (stoneTrack <= 0) stoneTrack = 3;
            if (stoneLocked <= 0) stoneLocked = 2;
            if (slamPrepare <= 0) slamPrepare = 4;
            if (chargePrepare <= 0) chargePrepare = 4;
            if (resonancePrepare <= 0) resonancePrepare = 6;
            if (shieldPrepare <= 0) shieldPrepare = 3.6f;
            if (shieldAngle <= 0) shieldAngle = 120;
            if (shieldRange <= 0) shieldRange = 10;
            if(shieldCrossHalfWidth<=0)shieldCrossHalfWidth=1.6f;
            if (lineTrack <= 0) lineTrack = 2.4f;
            if (lineLocked <= 0) lineLocked = 1.2f;
            if (lineWidth <= 0) lineWidth = 4;
            if (lineLength <= 0) lineLength = 24;
            if (counterWindowSeconds <= 0) counterWindowSeconds = 2;
            if (overloadSeconds <= 0) overloadSeconds = 10;
            if(stoneWaveInterval<=0)stoneWaveInterval=.75f;
            if(stoneWaves<1)stoneWaves=6;
            ApplyFastTempo();
        }

        public int tempoRevision;
        public float stoneWaveInterval=.75f;
        public int stoneWaves=6;
        public void ApplyFastTempo()
        {
            if(tempoRevision>=2)return;
            if(tempoRevision<1)
            {
                GuardianTempo.Replace(ref initialRest,2,1.2f);
                GuardianTempo.Replace(ref recoverySeconds,5,1.8f);
                GuardianTempo.Replace(ref recoverySecondsHeavy,5.5f,2);
                GuardianTempo.Replace(ref punishmentPrepare,3,2.1f);
                GuardianTempo.Replace(ref sweepPrepare,4,2.2f);
                GuardianTempo.Replace(ref stoneTrack,3,1.5f);
                GuardianTempo.Replace(ref stoneLocked,2,1.32f);
                GuardianTempo.Replace(ref slamPrepare,4,2.4f);
                GuardianTempo.Replace(ref chargePrepare,4,2.4f);
                GuardianTempo.Replace(ref resonancePrepare,6,3.6f);
                GuardianTempo.Replace(ref shieldPrepare,3.6f,2.2f);
                GuardianTempo.Replace(ref lineTrack,2.4f,1.8f);
                GuardianTempo.Replace(ref lineLocked,1.2f,.75f);
            }
            GuardianTempo.Replace(ref recoverySeconds,1.8f,1.08f);
            GuardianTempo.Replace(ref recoverySecondsHeavy,2,1.2f);
            tempoRevision=2;
        }

        public float punishmentDamage = 850, sweepDamage = 650, slamDamage = 650;
        public float stoneRadius = 2.5f, stoneDamage = 520, resonanceRadius = 4, resonanceDamage = 1800;
        public float slamRadius = 8, chargeHalfWidth = 2.4f, chargeDamage = 700, chargeSpeed = 22;
        public float staggerRequired = 350, overloadFailureDamage = 650;
        public float aiReactionSeconds = .65f, aiLockReactionSeconds = .2f, aiBackupDelay = .65f;
        public float tempo = 1.2f;
        public float initialRest = 2f, recoverySecondsHeavy = 5.5f;
        public float punishmentPrepare = 3, sweepPrepare = 4, stoneTrack = 3, stoneLocked = 2;
        public float slamPrepare = 4, chargePrepare = 4, resonancePrepare = 6;
        public float shieldPrepare = 3.6f, shieldAngle = 120, shieldRange = 10, shieldDamage = 420, shieldKnockback = 3;
        public float shieldCrossHalfWidth=1.6f; // ShieldPush's serialized ID now presents a cross-shaped ground shockwave.
        public float lineTrack = 2.4f, lineLocked = 1.2f, lineWidth = 4, lineLength = 24, lineDamage = 480;
        public float counterWindowSeconds = 2, overloadSeconds = 10;
        public bool temporaryStatueVisuals = true;
        public bool aiCounters = true; // Disable only to practice the player's counter; does not extend the window.
        public float Scaled(float seconds) => seconds / Math.Max(.1f, tempo);
        public float Duration(GuardianPattern pattern) => pattern == GuardianPattern.Overload ? overloadSeconds : Scaled(pattern switch
        {
            GuardianPattern.Punishment => punishmentPrepare, GuardianPattern.Sweep => sweepPrepare,
            GuardianPattern.FallingStone => stoneTrack, GuardianPattern.PlazaSlam => slamPrepare,
            GuardianPattern.GateCharge => chargePrepare, GuardianPattern.Resonance => resonancePrepare,
            GuardianPattern.ShieldPush => shieldPrepare, GuardianPattern.LineFracture => lineTrack,
            _ => 4
        });
        public float LockedDuration(GuardianPattern pattern) => Scaled(pattern == GuardianPattern.FallingStone ? stoneLocked : lineLocked);
        public float Recovery(GuardianPattern pattern) => pattern == GuardianPattern.Punishment || pattern == GuardianPattern.Resonance ||
            pattern == GuardianPattern.GateCharge || pattern == GuardianPattern.ShieldPush || pattern == GuardianPattern.Overload ?
            recoverySecondsHeavy : Scaled(recoverySeconds);
    }

    // Hard owns a separate serialized combat preset. These are initial tuning values; HP is
    // temporary for development, not an established balance target or a hidden enrage.
    [Serializable]
    public sealed class GuardianHardSettings
    {
        public GuardianNormalSettings combat = new GuardianNormalSettings
        {
            health = 40000, tempo = 1, recoverySeconds = 5.5f, recoverySecondsHeavy = 6,
            punishmentDamage = 950, sweepDamage = 730, slamDamage = 730,
            stoneDamage = 600, resonanceDamage = 2000, chargeDamage = 800,
            shieldDamage = 480, lineDamage = 550, overloadFailureDamage = 1050,
            counterWindowSeconds = 1.6f, overloadSeconds = 9
        };
        public float punishmentSecondDelay = 2, sweepSecondDelay = .85f, slamRingDelay = 1.05f;
        // The boss pose and cast name are visible throughout preparation. Only the ground shape is delayed.
        public float impactWarningSeconds = .4f, impactAfterimageSeconds = .08f;
        public float stoneCastSeconds = 2, stoneTrackSeconds = 1.1f, stoneLockedSeconds = .7f, stoneWaveGapSeconds = .2f;
        public int stoneWaves = 4;
        public float resonanceKnockback = 5;
        public float slamRingInner = 8f, slamRingOuter = 17f;
        public float wallRadius = 12, wallHeight = 3;
        public int wallSegments = 24;
        public float lockdownLockAt = 3, lockdownLineAt = 5, lockdownEraseAt = 11;
        public float aimTurnDegreesPerSecond = 90;
        public float lockdownWidth = 4, lockdownLineDamage = 600, lockdownFailureDamage = 1200;
        public float transformationSeconds = 4, postFailureRecovery = 6, fullRecovery = 8;
        // One introductory tank clash after the transformation. Values are independent of combat tempo.
        public float clashPrepareSeconds = 2, clashCounterSeconds = 1.8f, clashHoldSeconds = 6;
        public float clashPulseInterval = 2, clashPulseDamage = 430;
        public float clashShockwaveDamage = 620, clashFailureDamage = 900;
        public float clashOpeningSeconds = 2, clashRecoverySeconds = 5, clashCircleRadius = 4.2f;
        public float twistedSweepPrepare = 4, twistedSlamPrepare = 4, twistedGap = .9f;
        public float aerialLeapSeconds = .35f, aerialAimSeconds = 1f, aerialChargeSpeed = 65;
        public float aerialReturnSeconds = .45f;
        public int aerialChargePasses = 3;
        public float lureWalkSpeed = 9, lureRescueWindow = 1.5f, lureRescueRange = 25;
        public bool temporaryCorruptedVisuals = true;
        public int tempoRevision;
        public float lockdownReadyImpactSeconds=.55f;
        public void EnsureDefaults()
        {
            if (combat == null) combat = new GuardianHardSettings().combat;
            // Migrate the previous hard preset without changing the normal preset or custom HP.
            if (combat.health <= 0 || combat.health == 30000) combat.health = 40000;
            if (combat.autoDamage <= 0) combat.autoDamage = 120;
            if (combat.recoverySeconds <= 0) combat.recoverySeconds = 5.5f;
            if (combat.recoverySecondsHeavy <= 0) combat.recoverySecondsHeavy = 6;
            if (combat.punishmentPrepare <= 0) combat.punishmentPrepare = 3;
            if (combat.sweepPrepare <= 0) combat.sweepPrepare = 4;
            if (combat.slamPrepare <= 0) combat.slamPrepare = 4;
            if (combat.chargePrepare <= 0) combat.chargePrepare = 4;
            if (combat.stoneTrack <= 0) combat.stoneTrack = 3;
            if (combat.stoneLocked <= 0) combat.stoneLocked = 2;
            if (combat.resonancePrepare <= 0) combat.resonancePrepare = 6;
            if (combat.lineTrack <= 0) combat.lineTrack = 2.4f;
            if (combat.lineLocked <= 0) combat.lineLocked = 1.2f;
            if (combat.staggerRequired <= 0) combat.staggerRequired = 350;
            if (combat.punishmentDamage <= 0) combat.punishmentDamage = 950;
            if (combat.sweepDamage <= 0) combat.sweepDamage = 730;
            if (combat.slamDamage <= 0) combat.slamDamage = 730;
            if (combat.stoneDamage <= 0) combat.stoneDamage = 600;
            if (combat.resonanceDamage <= 0) combat.resonanceDamage = 2000;
            if (combat.chargeDamage <= 0) combat.chargeDamage = 800;
            if (combat.shieldDamage <= 0) combat.shieldDamage = 480;
            if (combat.lineDamage <= 0) combat.lineDamage = 550;
            if (combat.overloadFailureDamage <= 0) combat.overloadFailureDamage = 1050;
            if (combat.stoneRadius <= 0) combat.stoneRadius = 2.5f;
            if (combat.resonanceRadius <= 0) combat.resonanceRadius = 4;
            if (combat.slamRadius <= 0) combat.slamRadius = 8;
            if (combat.chargeHalfWidth <= 0) combat.chargeHalfWidth = 2.4f;
            if (combat.chargeSpeed <= 0) combat.chargeSpeed = 22;
            if (combat.lineWidth <= 0) combat.lineWidth = 4;
            if (combat.lineLength <= 0) combat.lineLength = 24;
            if (combat.shieldPrepare <= 0) combat.shieldPrepare = 3.6f;
            if (combat.shieldAngle <= 0) combat.shieldAngle = 120;
            if (combat.shieldRange <= 0) combat.shieldRange = 10;
            if(combat.shieldCrossHalfWidth<=0)combat.shieldCrossHalfWidth=1.6f;
            if (combat.shieldKnockback <= 0) combat.shieldKnockback = 3;
            if (combat.tempo <= 0) combat.tempo = 1;
            if (combat.counterWindowSeconds <= 0) combat.counterWindowSeconds = 1.6f;
            if (combat.overloadSeconds <= 0) combat.overloadSeconds = 9;
            if (punishmentSecondDelay <= 0) punishmentSecondDelay = 2;
            if (sweepSecondDelay <= 0 || sweepSecondDelay == 3 || sweepSecondDelay == .9f) sweepSecondDelay = .55f;
            if (slamRingDelay <= 0 || slamRingDelay == 3 || slamRingDelay == .9f) slamRingDelay = .65f;
            if (impactWarningSeconds <= 0) impactWarningSeconds = .4f;
            if (impactAfterimageSeconds <= 0) impactAfterimageSeconds = .08f;
            if (stoneCastSeconds <= 0) stoneCastSeconds = 2;
            if (stoneTrackSeconds <= 0) stoneTrackSeconds = 1.1f;
            if (stoneLockedSeconds <= 0) stoneLockedSeconds = .7f;
            if (stoneWaveGapSeconds <= 0) stoneWaveGapSeconds = .2f;
            if (stoneWaves < 1) stoneWaves = 4;
            if (resonanceKnockback <= 0) resonanceKnockback = 5;
            // One shared boundary keeps the visual ring and center hit joined at exactly 8m.
            slamRingInner = combat.slamRadius;
            if (slamRingOuter <= slamRingInner) slamRingOuter = 17;
            if (wallRadius <= 0) wallRadius = 12;
            if (wallHeight <= 0) wallHeight = 3;
            if (wallSegments < 12) wallSegments = 24;
            if (lockdownLockAt <= 0) lockdownLockAt = 3;
            if (aimTurnDegreesPerSecond <= 0) aimTurnDegreesPerSecond = 90;
            if (lockdownLineAt <= lockdownLockAt) lockdownLineAt = lockdownLockAt + 2;
            if (lockdownEraseAt <= lockdownLineAt) lockdownEraseAt = lockdownLineAt + 6;
            if (lockdownWidth <= 0) lockdownWidth = 4;
            if (lockdownLineDamage <= 0) lockdownLineDamage = 600;
            if (lockdownFailureDamage <= 0) lockdownFailureDamage = 1200;
            if (transformationSeconds <= 0 || transformationSeconds == 8) transformationSeconds = 4;
            if (clashPrepareSeconds <= 0 || clashPrepareSeconds == 8 || clashPrepareSeconds == 11) clashPrepareSeconds = 2;
            if (clashCounterSeconds <= 0) clashCounterSeconds = 1.8f;
            if (clashHoldSeconds <= 0) clashHoldSeconds = 6;
            if (clashPulseInterval <= 0) clashPulseInterval = 2;
            if (clashPulseDamage <= 0) clashPulseDamage = 430;
            if (clashShockwaveDamage <= 0) clashShockwaveDamage = 620;
            if (clashFailureDamage <= 0) clashFailureDamage = 900;
            if (clashOpeningSeconds <= 0 || clashOpeningSeconds == 5) clashOpeningSeconds = 2;
            if (clashRecoverySeconds <= 0) clashRecoverySeconds = 5;
            if (clashCircleRadius <= 0) clashCircleRadius = 4.2f;
            if (postFailureRecovery <= 0) postFailureRecovery = 6;
            if (fullRecovery <= 0) fullRecovery = 8;
            if (twistedSweepPrepare <= 0) twistedSweepPrepare = 4;
            if (twistedSlamPrepare <= 0) twistedSlamPrepare = 4;
            if (twistedGap <= 0 || twistedGap == 3) twistedGap = .55f;
            if (aerialLeapSeconds <= 0 || aerialLeapSeconds == .65f) aerialLeapSeconds = .35f;
            if (aerialAimSeconds <= 0 || aerialAimSeconds == 1.5f) aerialAimSeconds = 1f;
            if (aerialChargeSpeed <= 0 || aerialChargeSpeed == 26) aerialChargeSpeed = 65;
            if (aerialReturnSeconds <= 0) aerialReturnSeconds = .45f;
            if (aerialChargePasses < 1) aerialChargePasses = 3;
            if (lureWalkSpeed <= 0) lureWalkSpeed = 9;
            if (lureRescueWindow <= 0) lureRescueWindow = 1.5f;
            if (lureRescueRange <= 0) lureRescueRange = 25;
            if(lockdownReadyImpactSeconds<=0)lockdownReadyImpactSeconds=.55f;
            ApplyFastTempo();
        }
        public void ApplyFastTempo()
        {
            if(tempoRevision>=2)return;
            if(tempoRevision<1)
            {
                GuardianTempo.Replace(ref combat.initialRest,2,1);
                GuardianTempo.Replace(ref combat.recoverySeconds,5.5f,1.2f);
                GuardianTempo.Replace(ref combat.recoverySecondsHeavy,6,1.6f);
                GuardianTempo.Replace(ref combat.punishmentPrepare,3,1.8f);
                GuardianTempo.Replace(ref combat.sweepPrepare,4,1.9f);
                GuardianTempo.Replace(ref combat.slamPrepare,4,2);
                GuardianTempo.Replace(ref combat.chargePrepare,4,2);
                GuardianTempo.Replace(ref combat.resonancePrepare,6,3);
                GuardianTempo.Replace(ref combat.shieldPrepare,3.6f,2);
                GuardianTempo.Replace(ref combat.lineTrack,2.4f,1.5f);
                GuardianTempo.Replace(ref combat.lineLocked,1.2f,.65f);
                GuardianTempo.Replace(ref punishmentSecondDelay,2,.85f);
                GuardianTempo.Replace(ref sweepSecondDelay,.85f,.55f);
                GuardianTempo.Replace(ref slamRingDelay,1.05f,.65f);
                GuardianTempo.Replace(ref stoneCastSeconds,2,1.2f);
                GuardianTempo.Replace(ref stoneLockedSeconds,.7f,.95f);
                GuardianTempo.Replace(ref stoneWaveGapSeconds,.2f,.55f);
                if(stoneWaves==4)stoneWaves=8;
                GuardianTempo.Replace(ref lockdownLockAt,3,1.8f);
                GuardianTempo.Replace(ref lockdownLineAt,5,2.7f);
                GuardianTempo.Replace(ref lockdownEraseAt,11,6);
                GuardianTempo.Replace(ref postFailureRecovery,6,1);
                GuardianTempo.Replace(ref fullRecovery,8,1.6f);
                GuardianTempo.Replace(ref clashRecoverySeconds,5,1.5f);
                GuardianTempo.Replace(ref twistedSweepPrepare,4,2.1f);
                GuardianTempo.Replace(ref twistedSlamPrepare,4,2.1f);
                GuardianTempo.Replace(ref twistedGap,.9f,.55f);
            }
            GuardianTempo.Replace(ref combat.recoverySeconds,1.2f,.75f);
            GuardianTempo.Replace(ref combat.recoverySecondsHeavy,1.6f,1);
            GuardianTempo.Replace(ref fullRecovery,1.6f,.9f);
            GuardianTempo.Replace(ref clashRecoverySeconds,1.5f,.9f);
            GuardianTempo.Replace(ref postFailureRecovery,1,.9f);
            tempoRevision=2;
        }
    }

    public static class GuardianTempo
    {
        // Migrate shipped timing only, once; authored custom tuning and subsequent edits survive.
        public static void Replace(ref float value,float previous,float next)
        {if(value==previous)value=next;}
        public static bool CanHastenErase(bool exitOpen,int living,int outside,float remaining,float finish)
            =>exitOpen&&living>0&&outside==living&&remaining>Math.Max(.1f,finish);
    }

    public static class GuardianShieldWaveGeometry
    {
        // Union of two centered rectangles; overlapping at the center is still one hit.
        public static bool Hits(float side,float forward,float halfWidth,float reach)
            =>Math.Abs(side)<=halfWidth&&Math.Abs(forward)<=reach||
              Math.Abs(forward)<=halfWidth&&Math.Abs(side)<=reach;
    }

    public sealed class GuardianStoneRainSchedule
    {
        public int Emitted {get;private set;}
        public int Total {get;private set;}
        public float NextAt {get;private set;}
        private float _interval;
        public bool Emitting=>Emitted<Total;
        public void Start(float now,float interval,int count)
        {Emitted=0;Total=Math.Max(1,Math.Min(16,count));NextAt=now;_interval=Math.Max(.1f,interval);}
        // At most one wave per tick: a delayed host never emits a backlog at one location.
        public bool TryEmit(float now)
        {if(!Emitting||now<NextAt)return false;Emitted++;NextAt=now+_interval;return true;}
        public void Clear(){Emitted=Total=0;NextAt=0;}
    }

    public static class GuardianHardGeometry
    {
        public static double TrackYaw(double current, double target, double degreesPerSecond, double seconds)
        {
            double delta=((target-current+180)%360+360)%360-180;
            double step=Math.Max(0,degreesPerSecond)*Math.Max(0,seconds);
            double result=current+Math.Max(-step,Math.Min(step,delta));
            return (result%360+360)%360;
        }

        public static bool LurePhase(GuardianPattern pattern, bool followup, int phase)
            => pattern == GuardianPattern.PlazaSlam && !followup || pattern == GuardianPattern.TwistedExecution && phase == 2;
        public static bool RescueWindow(double remaining, double window) => remaining > .4 && remaining <= window;
        public static bool RescueOutside(double distance, double slamRadius, double memberRadius)
            => distance >= slamRadius + memberRadius + .2;
        public static double AerialChargeAngle(double baseDegrees, int pass) => baseDegrees + Math.Max(0,pass) * 120;
        public static double AerialChargeStartRadius(double arena, double bossRadius)
            => Math.Max(1,arena) + Math.Max(0,bossRadius) + 2;
        public static bool SweptChargeHits(double x, double z, double forwardX, double forwardZ,
            double length, double halfWidth, double actorRadius)
        {
            double along = Math.Max(0, Math.Min(Math.Max(0,length), x * forwardX + z * forwardZ));
            double dx = x - forwardX * along, dz = z - forwardZ * along;
            double radius = Math.Max(0,halfWidth) + Math.Max(0,actorRadius);
            return dx * dx + dz * dz <= radius * radius;
        }

        // A member must fully cross the wall boundary to escape the interior erase.
        public static bool LockdownEraseHits(double x, double z, double wallRadius, double actorRadius)
        {
            double radius = Math.Max(0, wallRadius) + Math.Max(0, actorRadius);
            return x * x + z * z <= radius * radius;
        }

        // Rally inside the visible crack, beside the aiming line. A straight
        // approach to this inner point stays inside the convex wall boundary.
        public static void LockdownRally(double x,double z,double breachX,double breachZ,
            double originX,double originZ,double forwardX,double forwardZ,double wallRadius,
            double actorRadius,double lineHalfWidth,out double targetX,out double targetZ)
        {
            targetX=x;targetZ=z;
            if(!LockdownEraseHits(x,z,wallRadius,actorRadius+.1))return;
            double length=Math.Sqrt(breachX*breachX+breachZ*breachZ);
            double facing=Math.Sqrt(forwardX*forwardX+forwardZ*forwardZ);
            if(length<.001||facing<.001)return;
            breachX/=length;breachZ/=length;forwardX/=facing;forwardZ/=facing;
            double inside=Math.Max(0,wallRadius-.21-Math.Max(0,actorRadius)-.2);
            double clear=Math.Max(0,lineHalfWidth)+Math.Max(0,actorRadius)+.35;
            double actorAlong=(x-originX)*forwardX+(z-originZ)*forwardZ;
            double actorSide=(x-originX)*forwardZ-(z-originZ)*forwardX;
            double best=double.PositiveInfinity;
            for(int sign=-1;sign<=1;sign+=2)
            for(int degrees=0;degrees<=90;degrees+=2)
            {
                double angle=sign*degrees*Math.PI/180,cos=Math.Cos(angle),sin=Math.Sin(angle);
                double px=(breachX*cos-breachZ*sin)*inside,pz=(breachX*sin+breachZ*cos)*inside;
                double along=(px-originX)*forwardX+(pz-originZ)*forwardZ;
                double side=(px-originX)*forwardZ-(pz-originZ)*forwardX;
                if(along>=0&&along<=wallRadius*2&&Math.Abs(side)<clear)continue;
                // Keep an already safe member on its side of the locked line.
                if(actorAlong>=0&&Math.Abs(actorSide)>=clear&&side*actorSide<0)continue;
                double cost=(px-x)*(px-x)+(pz-z)*(pz-z);
                if(cost<best){best=cost;targetX=px;targetZ=pz;}
                break; // Nearest safe angle to the crack on this side.
            }
        }
        public static void LockdownExit(double x,double z,double breachX,double breachZ,
            double wallRadius,int segments,double actorRadius,out double targetX,out double targetZ)
        {
            targetX=x;targetZ=z;
            if(!LockdownEraseHits(x,z,wallRadius,actorRadius+.1))return;
            double length=Math.Sqrt(breachX*breachX+breachZ*breachZ);
            if(length<.001)return;
            breachX/=length;breachZ/=length;
            double lateral=Math.Abs(x*breachZ-z*breachX);
            double gap=Math.Max(.05,Math.PI*wallRadius/Math.Max(12,segments)-.015-Math.Max(0,actorRadius)-.1);
            double radius=lateral<=gap*.8?wallRadius+3.2:Math.Max(0,wallRadius-.21-Math.Max(0,actorRadius)-.2);
            targetX=breachX*radius;targetZ=breachZ*radius;
        }

        public static int[] ThreeCracks(int segments, int rotation, int first, int second, int third)
        {
            int count = Math.Max(12, segments), sector = count / 3;
            int[] offsets = { first, second, third }, result = new int[3];
            for (int i = 0; i < 3; i++)
                result[i] = ((rotation % count + count) % count + i * sector +
                    Math.Max(1, Math.Min(sector - 2, offsets[i]))) % count;
            return result;
        }
        public static bool LineHitsCrack(double originX, double originZ, double forwardX, double forwardZ,
            double crackX, double crackZ, double radius, int segments, double lineHalfWidth = 0)
        {
            double x = crackX * radius - originX, z = crackZ * radius - originZ;
            double along = x * forwardX + z * forwardZ;
            double side = Math.Abs(x * forwardZ - z * forwardX);
            double crackHalfWidth = radius * Math.Sin(Math.PI / Math.Max(12, segments));
            return along > 0 && side <= Math.Max(0, lineHalfWidth) + crackHalfWidth;
        }
    }

    public static class GuardianClashRules
    {
        public static bool OpeningActive(int phase, GuardianStep step) => phase == 4 && step == GuardianStep.Groggy;
        public static double JumpHeight(double progress)
        {
            double p = Math.Max(0, Math.Min(1, progress));
            double t = p < .25 ? p / .25 : p < .7 ? 1 : (1 - p) / .3;
            return 6 * t * t * (3 - 2 * t);
        }

        public static bool TransformationActive(bool hard, GuardianPattern pattern, int clashPhase)
            => hard && pattern == GuardianPattern.Transformation && clashPhase == 0;

        public static bool CounterEligible(bool windowOpen, bool alive, bool tank, double distanceFromCenter, double circleRadius)
            => windowOpen && alive && tank && distanceFromCenter >= 0 && distanceFromCenter <= circleRadius;
    }

    // Thresholds are consumed only when their mechanic actually starts. A fast HP drop queues
    // them in descending threshold order, while death cancels the whole encounter.
    public sealed class GuardianHardState
    {
        public bool LockdownUsed { get; private set; }
        public bool TransformationUsed { get; private set; }
        public bool FullLockdownUsed { get; private set; }
        public bool Transformed { get; private set; }
        private int _transformedIntroduction, _postTransformSelection;
        public void Reset()
        { LockdownUsed = TransformationUsed = FullLockdownUsed = Transformed = false; _transformedIntroduction = _postTransformSelection = 0; }
        public bool HasPendingMilestone(float ratio) => ratio > 0 &&
            (ratio <= .7f && !LockdownUsed ||
             ratio <= .4f && !TransformationUsed || Transformed && ratio <= .25f && !FullLockdownUsed);
        public bool TryStartTransformation(float ratio)
        {
            if (ratio <= 0 || ratio > .4f || TransformationUsed || !LockdownUsed)
                return false;
            TransformationUsed = true;
            return true;
        }
        public GuardianPattern Next(float ratio)
        {
            if (ratio <= 0) return GuardianPattern.None;
            if (ratio <= .7f && !LockdownUsed) { LockdownUsed = true; return GuardianPattern.Lockdown; }
            if (ratio <= .4f && !TransformationUsed) { TransformationUsed = true; return GuardianPattern.Transformation; }
            if (!Transformed) return GuardianPattern.None;
            if (_transformedIntroduction < 3)
                return ++_transformedIntroduction switch { 1 => GuardianPattern.Sweep, 2 => GuardianPattern.PlazaSlam, _ => GuardianPattern.AerialCharge };
            if (ratio <= .25f && !FullLockdownUsed) { FullLockdownUsed = true; return GuardianPattern.FullLockdown; }
            return (++_postTransformSelection % 4) switch
            { 1 => GuardianPattern.TwistedExecution, 2 => GuardianPattern.AerialCharge, _ => GuardianPattern.None };
        }
        public void FinishTransformation() => Transformed = true;
    }

    // Encounter state is independent of animation, damage and networking. Absolute host combat time.
    public sealed class GuardianNormalState
    {
        public static readonly GuardianPattern[] Introduction = { GuardianPattern.Punishment, GuardianPattern.Sweep,
            GuardianPattern.ShieldPush, GuardianPattern.FallingStone, GuardianPattern.LineFracture,
            GuardianPattern.PlazaSlam, GuardianPattern.GateCharge, GuardianPattern.Resonance };
        public GuardianPattern Pattern { get; private set; }
        public GuardianStep Step { get; private set; }
        public float Started { get; private set; }
        public float Ends { get; private set; }
        public int Serial { get; private set; }
        public int Introduced { get; private set; }
        public bool OverloadPending { get; private set; }
        public bool OverloadUsed { get; private set; }
        public float Stagger { get; private set; }
        private GuardianPattern _previous;
        private readonly int[] _seen = new int[Introduction.Length];
        public void Reset(float now, GuardianNormalSettings settings = null)
        {
            Pattern = _previous = GuardianPattern.None; Introduced = 0; Array.Clear(_seen, 0, _seen.Length);
            OverloadPending = OverloadUsed = false; Stagger = 0;
            Enter(GuardianStep.Rest, now, settings == null ? 2 : settings.Scaled(settings.initialRest));
        }
        public void ObserveHealth(float ratio)
        { } // Health thresholds no longer queue a stagger check.
        public GuardianPattern Choose(int random)
        {
            if (Introduced < Introduction.Length)
            {
                int next=Introduced;
                Introduced++; _seen[next]++; return _previous=Introduction[next];
            }
            // Draw among the least-seen eligible attacks. Every pattern stays in rotation.
            int start = (random & int.MaxValue) % Introduction.Length, chosen = -1, count = int.MaxValue;
            for (int i=0;i<Introduction.Length;i++)
            {
                int index=(start+i)%Introduction.Length;
                var candidate=Introduction[index];
                if(candidate==_previous || Heavy(candidate)&&Heavy(_previous))continue;
                if(_seen[index]<count){count=_seen[index];chosen=index;}
            }
            if(chosen<0)throw new InvalidOperationException("No guardian pattern eligible");
            _seen[chosen]++; return _previous=Introduction[chosen];
        }
        private static bool Heavy(GuardianPattern p) => p == GuardianPattern.Punishment || p == GuardianPattern.Resonance;
        public void Begin(GuardianPattern pattern, float now, GuardianNormalSettings settings)
        {
            if (pattern == GuardianPattern.Overload) { Rest(now,settings); return; }
            Pattern = pattern; Stagger = 0;
            Enter(pattern == GuardianPattern.Overload ? GuardianStep.StaggerCheck : GuardianStep.Telegraph, now, settings.Duration(pattern));
        }
        public void Enter(GuardianStep step, float now, float seconds)
        { Step = step; Started = now; Ends = now + Math.Max(0, seconds); Serial++; }
        public bool Due(float now) => now >= Ends;
        public float Remaining(float now) => Math.Max(0, Ends - now);
        public bool AddStagger(float value, float now, GuardianNormalSettings settings)
            => false;
        public bool Counter(float now)
        {
            if (Pattern != GuardianPattern.GateCharge && Pattern != GuardianPattern.FullLockdown || Step != GuardianStep.CounterWindow || Due(now)) return false;
            Enter(GuardianStep.Groggy, now, 2); return true;
        }
        public void Rest(float now, GuardianNormalSettings settings)
        { var finished=Pattern; Pattern = GuardianPattern.None; Enter(GuardianStep.Rest, now, settings.Recovery(finished)); }
        public void Stop() { Pattern = GuardianPattern.None; OverloadPending = false; Enter(GuardianStep.Stopped, 0, 0); }
        public static float SharedDamage(float total, int livingInRange) => livingInRange > 0 ? total / livingInRange : 0;
        public static bool InRectangle(float side, float forward, float halfWidth, float length)
            => Math.Abs(side) <= halfWidth && forward >= 0 && forward <= length;
    }
}
