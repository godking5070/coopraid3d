using System;

namespace RaidRogue
{
    public enum GuardianPattern { None, Punishment, Sweep, FallingStone, PlazaSlam, GateCharge, Resonance, Overload, ShieldPush, LineFracture, Lockdown, Transformation, TwistedExecution, FullLockdown }
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
            if (lineTrack <= 0) lineTrack = 2.4f;
            if (lineLocked <= 0) lineLocked = 1.2f;
            if (lineWidth <= 0) lineWidth = 4;
            if (lineLength <= 0) lineLength = 24;
            if (counterWindowSeconds <= 0) counterWindowSeconds = 2;
            if (overloadSeconds <= 0) overloadSeconds = 10;
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
            health = 30000, tempo = 1, recoverySeconds = 5.5f, recoverySecondsHeavy = 6,
            punishmentDamage = 950, sweepDamage = 730, slamDamage = 730,
            stoneDamage = 600, resonanceDamage = 2000, chargeDamage = 800,
            shieldDamage = 480, lineDamage = 550, overloadFailureDamage = 1050,
            counterWindowSeconds = 1.6f, overloadSeconds = 9
        };
        public float punishmentSecondDelay = 2, sweepSecondDelay = 3, slamRingDelay = 3;
        public float slamRingInner = 7.8f, slamRingOuter = 17f;
        public float wallRadius = 12, wallHeight = 3;
        public int wallSegments = 24;
        public float lockdownLockAt = 3, lockdownLineAt = 5, lockdownEraseAt = 11;
        public float lockdownWidth = 4, lockdownLineDamage = 600, lockdownFailureDamage = 1200;
        public float transformationSeconds = 8, postFailureRecovery = 6, fullRecovery = 8;
        public float twistedSweepPrepare = 4, twistedSlamPrepare = 4, twistedGap = 3;
        public bool temporaryCorruptedVisuals = true;
        public void EnsureDefaults()
        {
            if (combat == null) combat = new GuardianHardSettings().combat;
            if (combat.health <= 0) combat.health = 30000;
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
            if (combat.shieldKnockback <= 0) combat.shieldKnockback = 3;
            if (combat.tempo <= 0) combat.tempo = 1;
            if (combat.counterWindowSeconds <= 0) combat.counterWindowSeconds = 1.6f;
            if (combat.overloadSeconds <= 0) combat.overloadSeconds = 9;
            if (punishmentSecondDelay <= 0) punishmentSecondDelay = 2;
            if (sweepSecondDelay <= 0) sweepSecondDelay = 3;
            if (slamRingDelay <= 0) slamRingDelay = 3;
            if (slamRingInner <= 0) slamRingInner = 7.8f;
            if (slamRingOuter <= slamRingInner) slamRingOuter = 17;
            if (wallRadius <= 0) wallRadius = 12;
            if (wallHeight <= 0) wallHeight = 3;
            if (wallSegments < 12) wallSegments = 24;
            if (lockdownLockAt <= 0) lockdownLockAt = 3;
            if (lockdownLineAt <= lockdownLockAt) lockdownLineAt = lockdownLockAt + 2;
            if (lockdownEraseAt <= lockdownLineAt) lockdownEraseAt = lockdownLineAt + 6;
            if (lockdownWidth <= 0) lockdownWidth = 4;
            if (lockdownLineDamage <= 0) lockdownLineDamage = 600;
            if (lockdownFailureDamage <= 0) lockdownFailureDamage = 1200;
            if (transformationSeconds <= 0) transformationSeconds = 8;
            if (postFailureRecovery <= 0) postFailureRecovery = 6;
            if (fullRecovery <= 0) fullRecovery = 8;
            if (twistedSweepPrepare <= 0) twistedSweepPrepare = 4;
            if (twistedSlamPrepare <= 0) twistedSlamPrepare = 4;
            if (twistedGap <= 0) twistedGap = 3;
        }
    }

    public static class GuardianHardGeometry
    {
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

    // Thresholds are consumed only when their mechanic actually starts. A fast HP drop queues
    // them in descending threshold order, while death cancels the whole encounter.
    public sealed class GuardianHardState
    {
        public bool LockdownUsed { get; private set; }
        public bool OverloadUsed { get; private set; }
        public bool TransformationUsed { get; private set; }
        public bool FullLockdownUsed { get; private set; }
        public bool Transformed { get; private set; }
        private int _transformedIntroduction, _postTransformSelection;
        public void Reset()
        { LockdownUsed = OverloadUsed = TransformationUsed = FullLockdownUsed = Transformed = false; _transformedIntroduction = _postTransformSelection = 0; }
        public GuardianPattern Next(float ratio)
        {
            if (ratio <= 0) return GuardianPattern.None;
            if (ratio <= .7f && !LockdownUsed) { LockdownUsed = true; return GuardianPattern.Lockdown; }
            if (ratio <= .5f && !OverloadUsed) { OverloadUsed = true; return GuardianPattern.Overload; }
            if (ratio <= .4f && !TransformationUsed) { TransformationUsed = true; return GuardianPattern.Transformation; }
            if (!Transformed) return GuardianPattern.None;
            if (_transformedIntroduction < 2)
                return ++_transformedIntroduction == 1 ? GuardianPattern.Sweep : GuardianPattern.GateCharge;
            if (ratio <= .25f && !FullLockdownUsed) { FullLockdownUsed = true; return GuardianPattern.FullLockdown; }
            return Transformed && ++_postTransformSelection % 3 == 1 ? GuardianPattern.TwistedExecution : GuardianPattern.None;
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
        { if (ratio > 0 && ratio <= .5f && !OverloadUsed) OverloadPending = true; }
        public GuardianPattern Choose(int random)
        {
            if (OverloadPending) { OverloadPending = false; OverloadUsed = true; return GuardianPattern.Overload; }
            if (Introduced < Introduction.Length)
            { int next=Introduced++; _seen[next]++; return _previous=Introduction[next]; }
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
            Pattern = pattern; Stagger = 0;
            Enter(pattern == GuardianPattern.Overload ? GuardianStep.StaggerCheck : GuardianStep.Telegraph, now, settings.Duration(pattern));
        }
        public void Enter(GuardianStep step, float now, float seconds)
        { Step = step; Started = now; Ends = now + Math.Max(0, seconds); Serial++; }
        public bool Due(float now) => now >= Ends;
        public float Remaining(float now) => Math.Max(0, Ends - now);
        public bool AddStagger(float value, float now, GuardianNormalSettings settings)
        {
            if (Step != GuardianStep.StaggerCheck || Due(now) || value <= 0) return false;
            Stagger += value;
            if (Stagger < settings.staggerRequired) return false;
            Enter(GuardianStep.Groggy, now, 8); return true;
        }
        public bool Counter(float now)
        {
            if (Pattern != GuardianPattern.GateCharge && Pattern != GuardianPattern.FullLockdown || Step != GuardianStep.CounterWindow || Due(now)) return false;
            Enter(GuardianStep.Groggy, now, 6); return true;
        }
        public void Rest(float now, GuardianNormalSettings settings)
        { var finished=Pattern; Pattern = GuardianPattern.None; Enter(GuardianStep.Rest, now, settings.Recovery(finished)); }
        public void Stop() { Pattern = GuardianPattern.None; OverloadPending = false; Enter(GuardianStep.Stopped, 0, 0); }
        public static float SharedDamage(float total, int livingInRange) => livingInRange > 0 ? total / livingInRange : 0;
        public static bool InRectangle(float side, float forward, float halfWidth, float length)
            => Math.Abs(side) <= halfWidth && forward >= 0 && forward <= length;
    }
}
