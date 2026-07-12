
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using CoreSystems.Support;
using static CoreSystems.Support.WeaponDefinition;
using static CoreSystems.Support.WeaponDefinition.AmmoDef;
using static CoreSystems.Support.WeaponDefinition.ModelAssignmentsDef;
using CoreSystems;
using VRage.Game.ModAPI;
using System.IO;

namespace Scripts
{
    partial class Parts
    {
        /// <summary>
        /// Call in Init!!!!!
        /// </summary>
        public static WeaponDefinition[] AutoGenerateOtherPartDefs()
        {
            var definitions = MyDefinitionManager.Static.GetAllDefinitions();

            var weaponDefinitions = new List<WeaponDefinition>();
            var addedSubtypes = new HashSet<string>();
            var modContextBlacklist = new HashSet<string>();

            // I know this is iterated in LoadData but it seemed easier to do it here rather than pass blacklist state in
            foreach (var mod in Session.I.Session.Mods)
            {
                if (MyAPIGateway.Utilities.FileExistsInModLocation(Path.Combine("Data", "NoWeaponcoreConversions.txt"), mod))
                {
                    Log.Line($"Mod {mod.Name} has NoWeaponcoreConversions.txt present, ignoring any vanilla weapons from that mod.");
                    var ctxt = mod.GetModContext();
                    modContextBlacklist.Add(ctxt.ModName + ctxt.ModPath); // using IMyModContext doesn't work :(
                }
            }

            foreach (var wepdef in Session.I.WeaponDefinitions)
            {
                foreach (var mtpt in wepdef.Assignments.MountPoints)
                    addedSubtypes.Add(mtpt.SubtypeId);
            }

            var modBlocks = new Dictionary<string, KeenDef>();
            var sounds = MyDefinitionManager.Static.GetSoundDefinitions();
            var physicalMaterials = MyDefinitionManager.Static.GetPhysicalMaterialDefinitions();

            foreach (var definition in definitions)
            {
                var def = definition as MyWeaponBlockDefinition;

                if (def != null
                    && !string.IsNullOrEmpty(def.Id.SubtypeName)
                    && !Session.I.VanillaSubtypes.Contains(def.Id.SubtypeName)
                    && !addedSubtypes.Contains(def.Id.SubtypeName)
                    && !Session.I.VanillaWeaponCompatible.Contains(def.Id.SubtypeName)
                    && !modContextBlacklist.Contains(def.Context.ModName + def.Context.ModPath))
                {
                    var keenDef = new KeenDef()
                    {
                        BlockDef = def,
                        WeaponDef = MyDefinitionManager.Static.GetWeaponDefinition(def.WeaponDefinitionId),
                        ContainerDef = null,
                        EntityComponents = new List<MyComponentDefinitionBase>(),
                        AmmoMagazines = new List<AmmoMagPair>(),
                    };

                    switch (def.Id.TypeId.ToString().Replace(MyObjectBuilderType.LEGACY_TYPE_PREFIX, ""))
                    {
                        case "InteriorTurret":
                            keenDef.Type = KeenDef.WeaponType.InteriorTurret;
                            break;
                        case "LargeGatlingTurret":
                            keenDef.Type = KeenDef.WeaponType.LargeGatlingTurret;
                            break;
                        case "LargeMissileTurret":
                            keenDef.Type = KeenDef.WeaponType.LargeMissileTurret;
                            break;
                        case "SmallGatlingGun":
                            keenDef.Type = KeenDef.WeaponType.SmallGatlingGun;
                            break;
                        case "SmallMissileLauncher":
                            keenDef.Type = KeenDef.WeaponType.SmallMissileLauncher;
                            break;
                        case "SmallMissileLauncherReload":
                            keenDef.Type = KeenDef.WeaponType.SmallMissileLauncherReload;
                            break;
                        case "Searchlight":
                            keenDef.Type = KeenDef.WeaponType.Searchlight;
                            break;
                        default:
                            keenDef.Type = KeenDef.WeaponType.None;
                            break;
                    }

                    foreach (var ammodef in keenDef.WeaponDef.AmmoMagazinesId)
                    {
                        var ammoMagDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(ammodef);
                        var ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(ammoMagDef.AmmoDefinitionId);

                        var physicalMaterial = MyDefinitionManager.Static.GetPhysicalMaterialDefinition(
                            $"MyObjectBuilder_PhysicalMaterialDefinition/{ammoDef.PhysicalMaterial}");

                        keenDef.AmmoMagazines.Add(new AmmoMagPair()
                        {
                            AmmoDef = ammoDef,
                            AmmoMagDef = ammoMagDef,
                            AmmoMaterial = physicalMaterial
                        });
                    }

                    if (MyDefinitionManager.Static.TryGetContainerDefinition(def.Id, out keenDef.ContainerDef))
                    {

                        foreach (var def2 in keenDef.ContainerDef.DefaultComponents)
                        {
                            MyComponentDefinitionBase componentDef;
                            if (def2.ForceCreate && MyDefinitionManager.Static.TryGetEntityComponentDefinition(
                                MyDefinitionId.Parse($"{def2.BuilderType}/{def2.SubtypeId}"), out componentDef))
                            {
                                keenDef.EntityComponents.Add(componentDef);
                            }
                        }
                    }

                    modBlocks.Add(
                        def.Id.SubtypeName == ""
                            ? def.Id.TypeId.ToString().Remove(0, 16)
                            : def.Id.SubtypeName,
                        keenDef
                    );
                }
            }

            foreach (var def in modBlocks)
            {
                WeaponDefinition WCDef;
                try
                {
                    string muzzleFlash;
                    bool loop;
                    Vector3D avgOffset;
                    int rofOverride;
                    WCDef = new WeaponDefinition()
                    {
                        Assignments = GetMountPointDef(def.Key, def.Value),
                        Animations = GetAnimationDef(def.Value, out muzzleFlash, out loop, out avgOffset),
                        Ammos = GetAmmoDefs(def.Value, out rofOverride),
                        ModPath = def.Value.BlockDef.Context.ModPath,
                    };
                    WCDef.HardPoint = GetHardPointDef(def.Value, sounds, muzzleFlash, loop, avgOffset, rofOverride);
                    WCDef.Targeting = GetTargetingDef(def.Value, WCDef);


                    def.Value.WeaponDef.AmmoMagazinesId = new MyDefinitionId[]
                    {
                        MyDefinitionId.Parse(def.Value.MissileLauncher ? "MyObjectBuilder_AmmoMagazine/Energy_Missile" :  "MyObjectBuilder_AmmoMagazine/Energy")
                    };
                    var bdef = def.Value.BlockDef;
                    var ctext = Localization.GetText("WeaponAutoConvertDesc") + bdef.DescriptionText;
                    bdef.DescriptionEnum = null;
                    bdef.DescriptionString = ctext;
                }
                catch (Exception ex)
                {
                    Log.Line($"Error when automatically generating weapon definition for {def.Key}:\n{ex}" , null, true);
                    continue;
                }
                WCDef.AutoGenerated = true;
                weaponDefinitions.Add(WCDef);

                Log.Line($"Weapon {def.Key} has been auto-converted from vanilla stats.");
            }

            return weaponDefinitions.ToArray();
        }

        static AmmoDef[] GetAmmoDefs(KeenDef def, out int rofOverride)
        {
            float ammoCostMW = -1;
            rofOverride = -1;
            foreach (var ec in def.EntityComponents)
            {
                if (ec is MyEntityCapacitorComponentDefinition)
                {
                    var capDef = ec as MyEntityCapacitorComponentDefinition;

                    var capacity = capDef.Capacity;
                    var rechargeDrawActual = capDef.ChargeMultiplier * capDef.RechargeDraw;

                    var chargeTime = capacity * 60 * 60 / rechargeDrawActual;
                    rofOverride = (int)Math.Ceiling(60f / chargeTime);

                    ammoCostMW = capDef.RechargeDraw;

                    ec.Enabled = false;
                    for (int i = 0; i < def.ContainerDef.DefaultComponents.Count; i++)
                    {
                        if (def.ContainerDef.DefaultComponents[i].BuilderType == typeof(MyObjectBuilder_EntityCapacitorComponent))
                        {
                            def.ContainerDef.DefaultComponents.RemoveAt(i);

                            break;
                        }
                    }
                    break;
                }
            }

            var damageMultiplier = def.WeaponDef.DamageMultiplier > 0 ? def.WeaponDef.DamageMultiplier : 1;
            var rangeMultiplier = def.WeaponDef.RangeMultiplier > 0 ? def.WeaponDef.RangeMultiplier : 1;

            var ammoDefs = new List<AmmoDef>();
            foreach (var ammoMag in def.AmmoMagazines)
            {
                var mdef = ammoMag.AmmoDef as MyMissileAmmoDefinition;
                string s = !string.IsNullOrEmpty(ammoMag.AmmoDef.EndOfLifeSound?.GetCueName()) ? ammoMag.AmmoDef.EndOfLifeSound?.GetCueName() :
                    mdef != null ? "WepSmallMissileExpl" : "ImpMetalMetalCat0";

                string voxelSound = s;
                string hitSoundMetal = s;
                string playerHitSound = s;
                string floatingHitSound = s;

                const string DEFAULT_HIT_PARTICLE = "Explosion_Missile";
                string HitParticleAlt = mdef != null && !string.IsNullOrEmpty(mdef.EndOfLifeEffect) ? mdef.EndOfLifeEffect : DEFAULT_HIT_PARTICLE;

                List<GraphicDef.DecalDef.TextureMapDef> decalMaps = new List<GraphicDef.DecalDef.TextureMapDef>();

                Dictionary<MyStringHash, MyPhysicalMaterialDefinition.CollisionProperty> collisionProperties;
                if (ammoMag?.AmmoMaterial?.CollisionProperties != null &&
                    ammoMag.AmmoMaterial.CollisionProperties.TryGetValue(MyStringId.GetOrCompute("Hit"), out collisionProperties))
                {
                    foreach (var item in collisionProperties)
                    {
                        var str = item.Value.Sound?.GetCueName();
                        if (!string.IsNullOrEmpty(str))
                        {
                            switch (item.Key.ToString())
                            {
                                case "Metal":
                                case "Glass":
                                case "GlassOpaque":
                                    hitSoundMetal = str;
                                    if (!string.IsNullOrEmpty(item.Value.ParticleEffect))
                                        HitParticleAlt = item.Value.ParticleEffect;
                                    break;
                                case "Wolf":
                                case "Spider":
                                case "Character":
                                case "CharacterFemale":
                                    playerHitSound = str;

                                    if (!string.IsNullOrEmpty(item.Value.ParticleEffect) && HitParticleAlt == DEFAULT_HIT_PARTICLE)
                                        HitParticleAlt = item.Value.ParticleEffect;
                                    break;
                                case "Ammo":
                                    floatingHitSound = str;
                                    if (!string.IsNullOrEmpty(item.Value.ParticleEffect) && HitParticleAlt == DEFAULT_HIT_PARTICLE)
                                        HitParticleAlt = item.Value.ParticleEffect;
                                    break;
                                default:
                                    voxelSound = str;
                                    if (!string.IsNullOrEmpty(item.Value.ParticleEffect) && HitParticleAlt == DEFAULT_HIT_PARTICLE)
                                        HitParticleAlt = item.Value.ParticleEffect;
                                    break;
                            }
                        }

                        // No decals because keen stupid
                    }
                }

                var ammoDef = new AmmoDef()
                {
                    AmmoMagazine = ammoMag.AmmoMagDef.Id.SubtypeName,
                    AmmoRound = ammoMag.AmmoDef.Id.SubtypeName,
                    TerminalName = ammoMag.AmmoMagDef.DisplayNameText,
                    HardPointUsable = true,
                    NoGridOrArmorScaling = false,
                    BackKickForce = ammoMag.AmmoDef.BackkickForce,
                    BaseDamage = 1,
                    DamageScales = new DamageScaleDef
                    {
                        MaxIntegrity = 0f, // Blocks with integrity higher than this value will be immune to damage from this projectile; 0 = disabled.
                        DamageVoxels = true, // Whether to damage voxels.
                        SelfDamage = false, // lol
                        HealthHitModifier = 1, // How much Health to subtract from another projectile on hit; defaults to 1 if zero or less.
                        VoxelHitModifier = 1, // Voxel damage multiplier; defaults to 1 if zero or less.
                        Characters = 1f, // Character damage multiplier; defaults to 1 if zero or less.
                                         // For the following modifier values: -1 = disabled (higher performance), 0 = no damage, 0.01f = 1% damage, 2 = 200% damage.
                        FallOff = new DamageScaleDef.FallOffDef
                        {
                            Distance = 0f, // Distance at which damage begins falling off.
                            MinMultipler = 1f, // Value from 0.0001f to 1f where 0.1f would be a min damage of 10% of base damage.
                        },
                        Grids = new DamageScaleDef.GridSizeDef //If both of these values are -1, a 4x buff to SG weapons firing at LG and 0.25x debuff to LG weapons firing at SG will apply
                        {
                            Large = 1f, // Multiplier for damage against large grids.
                            Small = 1f, // Multiplier for damage against small grids.
                        },
                        Armor = new DamageScaleDef.ArmorDef
                        {
                            Armor = 1f, // Multiplier for damage against all armor. This is multiplied with the specific armor type multiplier (light, heavy).
                            Light = 1f, // Multiplier for damage against light armor.
                            Heavy = 1f, // Multiplier for damage against heavy armor.
                            NonArmor = 1f, // Multiplier for damage against every else.
                        },
                        Shields = new DamageScaleDef.ShieldDef
                        {
                            Modifier = 1f, // Multiplier for damage against shields.
                            Type = DamageScaleDef.ShieldDef.ShieldType.Default, // Damage vs healing against shields; Default, Heal
                            BypassModifier = -1f, // 0-1 will bypass shields and apply that damage amount as a scaled %.  -1 is disabled.  -2 to -1 will alter the chance of penning a damaged shield, with -2 being a 100% reduction
                            HeatModifier = 1, // scales how much of the damage is converted to heat, negative values subtract heat.
                        },
                        DamageType = new DamageScaleDef.DamageTypes // Damage type of each element of the projectile's damage; Kinetic, Energy
                        {
                            Base = DamageScaleDef.DamageTypes.Damage.Kinetic, // Base Damage uses this
                            AreaEffect = DamageScaleDef.DamageTypes.Damage.Energy,
                            Detonation = DamageScaleDef.DamageTypes.Damage.Energy,
                            Shield = DamageScaleDef.DamageTypes.Damage.Energy, // Damage against shields is currently all of one type per projectile. Shield Bypass Weapons, always Deal Energy regardless of this line
                        },
                        Deform = new DamageScaleDef.DeformDef
                        {
                            DeformType = DamageScaleDef.DeformDef.DeformTypes.AllDamagedBlocks, // HitBlock- applies deformation to the block that was hit
                                                                                                // AllDamagedBlocks- applies deformation to all blocks damaged (for AOE)
                                                                                                // NoDeform- applies no deformation
                            DeformDelay = 15, // Time in ticks to wait before applying another deformation event (prevents excess calls for deformation every tick or from multiple sources)
                        },
                    },
                    AmmoAudio = new AmmoAudioDef
                    {
                        TravelSound = def.MissileLauncher ? def.WeaponDef.WeaponAmmoDatas[1]?.FlightSound?.GetCueName() ?? "" : "",
                        HitSound = hitSoundMetal,
                        FloatingHitSound = floatingHitSound,
                        HitPlayChance = 1f,
                        HitPlayShield = true,
                        PlayerHitSound = playerHitSound,
                        VoxelHitSound = voxelSound,
                        WaterHitSound = s,
                    },

                    Trajectory = new TrajectoryDef
                    {
                        Guidance = TrajectoryDef.GuidanceType.None,
                        DesiredSpeed = ammoMag.AmmoDef.DesiredSpeed,
                        MaxTrajectory = ammoMag.AmmoDef.MaxTrajectory * rangeMultiplier,
                        GravityMultiplier = 1f,
                        SpeedVariance = new Randomize { Start = -ammoMag.AmmoDef.SpeedVar, End = ammoMag.AmmoDef.SpeedVar },
                    },
                    AmmoGraphics = new GraphicDef
                    {
                        ModelName = "",
                        VisualProbability = 1f,
                        Lines = new GraphicDef.LineDef
                        {
                            DropParentVelocity = true,
                        }
                    }
                };

                if (ammoMag.AmmoDef is MyMissileAmmoDefinition)
                {
                    ammoDef.AmmoGraphics.ModelName = mdef.MissileModelName.Replace(def.BlockDef.Context.ModPath, ""); // lol
                    ammoDef.Mass = mdef.MissileMass;

                    

                    Log.Line($"{mdef.Id.SubtypeName}:" +
                        $" {ammoDef.Trajectory.DesiredSpeed}" +
                        $" {ammoDef.Trajectory.MaxLifeTime}" +
                        $" {ammoDef.Trajectory.AccelPerSec}");

                    if (mdef.MissileExplosionDamage > 0 && mdef.MissileExplosionRadius > 0)
                    {
                        var explosionFlags = mdef.ExplosionFlags ?? 0;

                        // not going to approximate vanilla explosion behaviour here because its stupidly cursed
                        ammoDef.AreaOfDamage.EndOfLife = new AreaOfDamageDef.EndOfLifeDef
                        {
                            Enable = true,
                            Radius = mdef.MissileExplosionRadius * 1.25f, // THANKS KEEN
                            Damage = mdef.MissileExplosionDamage * damageMultiplier,
                            Depth = 0f, // Max depth of AOE effect, in meters. 0=disabled, and AOE effect will reach to a depth of the radius value
                            MaxAbsorb = 0f, // Soft cutoff for damage (total, against shields or grids), except for pooled falloff.  If pooled falloff, limits max damage per block.
                            Falloff = AreaOfDamageDef.Falloff.Linear, //.NoFalloff applies the same damage to all blocks in radius
                                                                      //.Linear drops evenly by distance from center out to max radius
                                                                      //.Curve drops off damage sharply as it approaches the max radius
                                                                      //.InvCurve drops off sharply from the middle and tapers to max radius
                                                                      //.Squeeze does little damage to the middle, but rapidly increases damage toward max radius
                                                                      //.Pooled damage behaves in a pooled manner that once exhausted damage ceases.
                                                                      //.Exponential drops off exponentially.  Does not scale to max radius
                            ArmOnlyOnHit = !explosionFlags.HasFlag(MyExplosionFlags.FORCE_CUSTOM_END_OF_LIFE_EFFECT)
                            && !explosionFlags.HasFlag(MyExplosionFlags.CREATE_PARTICLE_EFFECT)
                            && string.IsNullOrEmpty(mdef.EndOfLifeEffect), // Detonation only is available, After it hits something, when this is true. IE, if shot down, it won't explode.
                            MinArmingTime = 0, // In ticks, before the Ammo is allowed to explode, detonate or similar; This affects shrapnel spawning.
                            NoVisuals = false,
                            NoSound = false,
                            ParticleScale = 1,
                            CustomParticle = HitParticleAlt, // Particle SubtypeID, from your Particle SBC
                                                            // If you need to set a custom offset, specify it in the "Hit" particle
                            CustomSound = hitSoundMetal, // SubtypeID from your Audio SBC, not a filename
                            Shape = mdef.MissileExplosionRadius < 4.25f ? AreaOfDamageDef.AoeShape.Diamond : AreaOfDamageDef.AoeShape.Round, // Round or Diamond shape.  Diamond is more performance friendly.
                        };
                        ammoDef.DamageScales.DamageVoxels = explosionFlags.HasFlag(MyExplosionFlags.AFFECT_VOXELS);
                    }



                    if (!mdef.MissileSkipAcceleration && mdef.MissileAcceleration > 0)
                        ammoDef.Trajectory.AccelPerSec = mdef.MissileAcceleration;
                    else if (!mdef.MissileSkipAcceleration && mdef.DesiredSpeed > mdef.MissileInitialSpeed)
                        ammoDef.Trajectory.AccelPerSec = 0.01f;
                    else if (mdef.MissileSkipAcceleration)
                        ammoDef.Trajectory.DesiredSpeed = Math.Min(mdef.DesiredSpeed, mdef.MissileInitialSpeed);

                    ammoDef.Trajectory.MaxLifeTime = mdef.MissileSkipAcceleration ?
                        (int)(120 * mdef.MaxTrajectory / Math.Min(mdef.DesiredSpeed, mdef.MissileInitialSpeed))
                        : (int)(120 * AccelTTICalc(mdef.MissileInitialSpeed, mdef.DesiredSpeed, mdef.MissileAcceleration, mdef.MaxTrajectory));

                    

                    if (!mdef.MissileGravityEnabled)
                        ammoDef.Trajectory.GravityMultiplier = 0f;

                    if (mdef.MissileHealthPool * damageMultiplier > 0)
                    {
                        ammoDef.BaseDamage = mdef.MissileHealthPool * damageMultiplier;
                    }

                    ammoDef.AmmoGraphics.Particles.Ammo = new ParticleDef
                    {
                        Name = !string.IsNullOrEmpty(mdef.MissileTrailEffect) ? mdef.MissileTrailEffect : "Smoke_Missile",
                        ApplyToShield = true,
                        Color = new Vector4(1),
                        Offset = new Vector3D(0),
                        Extras = new ParticleOptionDef
                        {
                            Loop = true,
                            MaxDistance = 20000f,
                            Scale = 1f,
                            MaxDuration = 9999,
                            Restart = true,
                        }
                    };

                    if (!ammoDef.AreaOfDamage.EndOfLife.Enable)
                    {
                        ammoDef.AmmoGraphics.Particles.Hit = new ParticleDef
                        {
                            Name = HitParticleAlt,
                            ApplyToShield = true,
                            Color = new Vector4(1),
                            Offset = new Vector3D(0),
                            Extras = new ParticleOptionDef
                            {
                                Loop = true,
                                MaxDistance = 20000f,
                                Scale = 1f,
                                MaxDuration = 9999,
                                Restart = true,
                            }
                        };
                    }
                }
                else
                {
                    var pdef = ammoMag.AmmoDef as MyProjectileAmmoDefinition;

                    ammoDef.ObjectsHit = new ObjectsHitDef
                    {
                        MaxObjectsHit = 1,
                        SkipBlocksForAOE = true,
                        CountBlocks = true,
                    };

                    ammoDef.Trajectory.MaxLifeTime = (int)(120 * pdef.MaxTrajectory / pdef.DesiredSpeed);

                    ammoDef.Mass = pdef.ProjectileHitImpulse / pdef.DesiredSpeed;

                    ammoDef.BaseDamage = pdef.ProjectileMassDamage * damageMultiplier;
                    ammoDef.DamageScales.Characters = pdef.ProjectileHealthDamage / pdef.ProjectileMassDamage;

                    if (pdef.ProjectileExplosionDamage > 0 && pdef.ProjectileExplosionRadius > 0)
                    {
                        // not going to approximate vanilla explosion behaviour here because its stupidly cursed
                        ammoDef.AreaOfDamage.EndOfLife = new AreaOfDamageDef.EndOfLifeDef
                        {
                            Enable = true,
                            Radius = pdef.ProjectileExplosionRadius * 1.25f, // THANKS KEEN
                            Damage = pdef.ProjectileExplosionDamage * damageMultiplier,
                            Depth = 0f, // Max depth of AOE effect, in meters. 0=disabled, and AOE effect will reach to a depth of the radius value
                            MaxAbsorb = 0f, // Soft cutoff for damage (total, against shields or grids), except for pooled falloff.  If pooled falloff, limits max damage per block.
                            Falloff = AreaOfDamageDef.Falloff.Linear, //.NoFalloff applies the same damage to all blocks in radius
                                                                      //.Linear drops evenly by distance from center out to max radius
                                                                      //.Curve drops off damage sharply as it approaches the max radius
                                                                      //.InvCurve drops off sharply from the middle and tapers to max radius
                                                                      //.Squeeze does little damage to the middle, but rapidly increases damage toward max radius
                                                                      //.Pooled damage behaves in a pooled manner that once exhausted damage ceases.
                                                                      //.Exponential drops off exponentially.  Does not scale to max radius
                            ArmOnlyOnHit = true, // Detonation only is available, After it hits something, when this is true. IE, if shot down, it won't explode.
                            MinArmingTime = 0, // In ticks, before the Ammo is allowed to explode, detonate or similar; This affects shrapnel spawning.
                            NoVisuals = false,
                            NoSound = false,
                            ParticleScale = 1,
                            CustomParticle = HitParticleAlt, // Particle SubtypeID, from your Particle SBC
                                                            // If you need to set a custom offset, specify it in the "Hit" particle
                            CustomSound = hitSoundMetal, // SubtypeID from your Audio SBC, not a filename
                            Shape = pdef.ProjectileExplosionRadius < 4.25f ? AreaOfDamageDef.AoeShape.Diamond : AreaOfDamageDef.AoeShape.Round, // Round or Diamond shape.  Diamond is more performance friendly.
                        };
                    }
                    else
                    {
                        ammoDef.AreaOfDamage.EndOfLife.NoVisuals = true;
                        ammoDef.AreaOfDamage.EndOfLife.NoSound = true;
                    }

                    var color = pdef.ProjectileTrailColor * 10; // me when the hardcode
                    ammoDef.AmmoGraphics.VisualProbability = pdef.ProjectileTrailProbability;

                    // cursed
                    /*double num2 = (float)(int)m_lengthMultiplier * m_projectileAmmoDefinition.ProjectileTrailScale;
                    num2 *= (double)(MyParticlesManager.Paused ? 0.6f : MyUtils.GetRandomFloat(0.6f, 0.8f));
                    if (num < num2)
                    {
                        num2 = num;
                    }

                    vector3D = ((m_state != 0 && !(num * num >= m_velocity_Combined.LengthSquared() * 0.01666666753590107 * (double)CHECK_INTERSECTION_INTERVAL)) ? (m_position - ((num - num2) * (double)MyUtils.GetRandomFloat(0f, 1f) + num2) * vector3D2) : (m_position - num2 * vector3D2));
                    if (Vector3D.DistanceSquared(vector3D, m_origin) < MINIMUM_DISTANCE_SQRD)
                    {
                        return;
                    }

                    float num3 = (MyParticlesManager.Paused ? 1f : MyUtils.GetRandomFloat(1f, 2f));
                    float num4 = (MyParticlesManager.Paused ? 0.2f : MyUtils.GetRandomFloat(0.2f, 0.3f)) * m_projectileAmmoDefinition.ProjectileTrailScale;
                    num4 *= MathHelper.Lerp(0.2f, 0.8f, MySector.MainCamera.Zoom.GetZoomLevel());
                    float num5 = 1f;
                    float num6 = 10f;
                    if (num2 > 0.0)
                    {
                        if (!string.IsNullOrEmpty(m_projectileAmmoDefinition.ProjectileTrailMaterial))
                        {
                            MyTransparentGeometry.AddLineBillboard(m_projectileAmmoDefinition.ProjectileTrailMaterialId, new Vector4(m_projectileAmmoDefinition.ProjectileTrailColor * num6, 1f), vector3D, vector3D2, (float)num2, num4);
                        }
                        else
                        {
                            MyTransparentGeometry.AddLineBillboard(ID_PROJECTILE_TRAIL_LINE, new Vector4(m_projectileAmmoDefinition.ProjectileTrailColor * num3 * num6, 1f) * num5, vector3D, vector3D2, (float)num2, num4);
                        }
                    }
                     */
                    var lineLength = 40 * pdef.ProjectileTrailScale * 0.7f; // 0.7 is avg, can't do length randomize here tho
                    var lineWidth = 0.2f * pdef.ProjectileTrailScale;

                    var colorVariance = string.IsNullOrEmpty(pdef.ProjectileTrailMaterial) ? 2f : 1f; // whjat in the actual

                    ammoDef.AmmoGraphics.Lines.Tracer = new GraphicDef.LineDef.TracerBaseDef
                    {
                        Enable = true,
                        Length = lineLength, //
                        Width = lineWidth, //
                        Color = new Vector4(color.X, color.Y, color.Z, 1), // RBG 255 is Neon Glowing, 100 is Quite Bright.
                        FactionColor = GraphicDef.LineDef.FactionColor.DontUse, // DontUse, Foreground, Background.
                        VisualFadeStart = 0, // Number of ticks the weapon has been firing before projectiles begin to fade their color
                        VisualFadeEnd = 0, // How many ticks after fade began before it will be invisible.
                        AlwaysDraw = lineLength > 300, // Prevents this tracer from being culled.  Only use if you have a reason too (very long tracers/trails).
                        Textures = new[] {// WeaponLaser, ProjectileTrailLine, WarpBubble, etc..
                            !string.IsNullOrEmpty(pdef.ProjectileTrailMaterial) ? pdef.ProjectileTrailMaterial : "ProjectileTrailLine", // Please always have this Line set if this Section is enabled.
                        },
                        TextureMode = GraphicDef.LineDef.Texture.Normal, // Normal, Cycle, Chaos, Wave
                    };
                    ammoDef.AmmoGraphics.Lines.WidthVariance = new Randomize { Start = 0, End = lineWidth * 1.5f };
                    ammoDef.AmmoGraphics.Lines.ColorVariance = new Randomize { Start = 1f, End = colorVariance };
                }
                if (ammoCostMW > 0)
                {
                    // costMW = EnergyCost * BaseDamage * (RateOfFire / 3600)
                    ammoDef.EnergyCost = ammoCostMW / (ammoDef.BaseDamage * (rofOverride / 3600f));
                    ammoDef.HybridRound = true;
                }
                
                ammoDefs.Add(ammoDef);
            }
            return ammoDefs.ToArray();
        }
        static AnimationDef GetAnimationDef(KeenDef def, out string muzzleFlash, out bool loop, out Vector3D avgOffset)
        {
            var events = new Dictionary<AnimationDef.PartAnimationSetDef.EventTriggers, AnimationDef.EventParticle[]>();

            Dictionary<string, MyTuple<bool, int, Vector3D>> firingFX = new Dictionary<string, MyTuple<bool, int, Vector3D>>();

            foreach (var particle in def.WeaponDef.WeaponEffects)
            {
                AnimationDef.PartAnimationSetDef.EventTriggers trigger = AnimationDef.PartAnimationSetDef.EventTriggers.Init;
                switch (particle.Action)
                {
                    case MyWeaponDefinition.WeaponEffectAction.BeforeShoot:
                        trigger = AnimationDef.PartAnimationSetDef.EventTriggers.PreFire;
                        break;
                    case MyWeaponDefinition.WeaponEffectAction.Shoot:
                        if (!particle.DisplayOnlyOnDummyFiring)
                            trigger = AnimationDef.PartAnimationSetDef.EventTriggers.Firing;

                        MyTuple<bool, int, Vector3D> num;
                        if (!firingFX.TryGetValue(particle.Particle, out num))
                            firingFX[particle.Particle] = new MyTuple<bool, int, Vector3D>(particle.Loop, 0, particle.Offset);
                        else
                            firingFX[particle.Particle] = new MyTuple<bool, int, Vector3D>(particle.Loop, num.Item2 + 1, particle.Offset); // hope nobody has different offsets lol
                        break;
                }

                if (trigger == AnimationDef.PartAnimationSetDef.EventTriggers.Init)
                    continue;

                var ep = new AnimationDef.EventParticle
                {
                    EmptyNames = new string[] { particle.Dummy },
                    MuzzleNames = new string[] { particle.Dummy },
                    ForceStop = particle.InstantStop,

                    Particle = new ParticleDef()
                    {
                        Name = particle.Particle,
                        Color = new Vector4(1),
                        Offset = particle.Offset,
                        Extras = new ParticleOptionDef()
                        {
                            Loop = particle.Loop,
                            Restart = false,
                        }
                    }
                };

                AnimationDef.EventParticle[] eventParticles;
                if (!events.TryGetValue(trigger, out eventParticles))
                {
                    eventParticles = new AnimationDef.EventParticle[]
                    {
                        ep
                    };
                    events[trigger] = eventParticles;
                }
                else
                {
                    // could be better but oh well
                    var list = eventParticles.ToList();
                    list.Add(ep);
                    eventParticles = list.ToArray();
                }
            }
            muzzleFlash = "";
            loop = false;
            avgOffset = Vector3D.Zero;
            int mostFound = int.MinValue;
            foreach (var str in firingFX)
            {
                if (str.Value.Item2 > mostFound)
                {
                    mostFound = str.Value.Item2;

                    muzzleFlash = str.Key;
                    loop = str.Value.Item1;
                    avgOffset = str.Value.Item3;
                }
            }


            return new AnimationDef()
            {
                EventParticles = events,
            };
        }
        static HardPointDef GetHardPointDef(KeenDef def, DictionaryValuesReader<MyDefinitionId, MyAudioDefinition> sounds, string muzzleFlash, bool loopMuzzleFlash, Vector3D muzzleFlashOffset, int rofOverride)
        {
            const float ROTATE_CONSTANT = 180f / (float)Math.PI;
            var weaponAmmoData = def.MissileLauncher ? 
                def.WeaponDef.WeaponAmmoDatas[1] != null ? def.WeaponDef.WeaponAmmoDatas[1] : def.WeaponDef.WeaponAmmoDatas[0] : 
                def.WeaponDef.WeaponAmmoDatas[0] != null ? def.WeaponDef.WeaponAmmoDatas[0] : def.WeaponDef.WeaponAmmoDatas[1];

            if (weaponAmmoData == null)
            {
                throw new Exception($"Error: Vanilla weapon definition {def.WeaponDef.Id.SubtypeName} has null weapon ammo data!");
            }

            float invAmount = -1f;
            bool useInvMult = true;

            foreach (var invDef in def.EntityComponents)
            {
                if (invDef is MyInventoryComponentDefinition)
                {
                    var invdef = invDef as MyInventoryComponentDefinition;

                    invAmount = invdef.Volume;
                    useInvMult = invdef.MultiplierEnabled;
                }
            }

            bool arcade = !MyAPIGateway.Session.SessionSettings.RealisticSound;

            var firingSound = weaponAmmoData.ShootSound?.GetCueName() ?? "";
            var soundPerShot = true;
            MyAudioDefinition firingSoundDef;
            if (sounds.TryGetValue(MyDefinitionId.Parse($"MyObjectBuilder_AudioDefinition/{firingSound}"), out firingSoundDef)
                || (arcade && sounds.TryGetValue(MyDefinitionId.Parse($"MyObjectBuilder_AudioDefinition/Arc{firingSound}"), out firingSoundDef))
                || (!arcade && sounds.TryGetValue(MyDefinitionId.Parse($"MyObjectBuilder_AudioDefinition/Real{firingSound}"), out firingSoundDef)))
            {
                var soundOB = firingSoundDef.GetObjectBuilder() as MyObjectBuilder_AudioDefinition;
                firingSound = firingSoundDef.Id.SubtypeName;
                soundPerShot = !soundOB.Loopable;
            }

            return new HardPointDef()
            {
                DefinitionPriority = int.MinValue,
                PartName = def.BlockDef.DisplayNameText,
                DeviateShotAngle = def.WeaponDef.DeviateShotAngle * ROTATE_CONSTANT,
                AimingTolerance = Math.Max(1, def.WeaponDef.DeviateShotAngle * ROTATE_CONSTANT),
                AimLeadingPrediction = HardPointDef.Prediction.Advanced,

                DelayCeaseFire = (int)(60f * def.WeaponDef.ReleaseTimeAfterFire / 1000f),

                Ui = new HardPointDef.UiDef(),
                Ai = new HardPointDef.AiDef()
                {
                    TrackTargets = def.IsTurret,
                    TurretAttached = def.IsTurret,
                    TurretController = def.IsTurret,
                    PrimaryTracking = true,
                },
                HardWare = new HardPointDef.HardwareDef()
                {
                    RotateRate = def.IsTurret ? ((MyLargeTurretBaseDefinition)def.BlockDef).RotationSpeed * ROTATE_CONSTANT : 1,
                    ElevateRate = def.IsTurret ? ((MyLargeTurretBaseDefinition)def.BlockDef).ElevationSpeed * ROTATE_CONSTANT : 1,
                    MinAzimuth = def.IsTurret ? ((MyLargeTurretBaseDefinition)def.BlockDef).MinAzimuthDegrees : -1,
                    MaxAzimuth = def.IsTurret ? ((MyLargeTurretBaseDefinition)def.BlockDef).MaxAzimuthDegrees : 1,
                    MinElevation = def.IsTurret ? ((MyLargeTurretBaseDefinition)def.BlockDef).MinElevationDegrees : -1,
                    MaxElevation = def.IsTurret ? ((MyLargeTurretBaseDefinition)def.BlockDef).MaxElevationDegrees : 1,
                    HomeAzimuth = 0,
                    HomeElevation = 0,
                    FixedInventorySize = useInvMult,
                    InventorySize = invAmount == -1 ? def.BlockDef.InventoryMaxVolume : invAmount,
                    IdlePower = 0.002f,
                    Offset = (def.BlockDef as MyLargeTurretBaseDefinition)?.AimingOffset ?? Vector3D.Zero,
                    Type = HardPointDef.HardwareDef.HardwareType.BlockWeapon,
                },
                Other = new HardPointDef.OtherDef()
                {
                    ProhibitLGTargeting = def.IsTurret && ((MyLargeTurretBaseDefinition)def.BlockDef).HiddenTargetingOptions.HasFlag(Sandbox.Common.ObjectBuilders.Definitions.MyTurretTargetingOptions.LargeShips),
                    ProhibitSGTargeting = def.IsTurret && ((MyLargeTurretBaseDefinition)def.BlockDef).HiddenTargetingOptions.HasFlag(Sandbox.Common.ObjectBuilders.Definitions.MyTurretTargetingOptions.SmallShips),
                },
                Loading = new HardPointDef.LoadingDef()
                {
                    RateOfFire = rofOverride != -1 ? rofOverride : weaponAmmoData.RateOfFire,
                    BarrelsPerShot = 1,
                    TrajectilesPerBarrel = def.Gatling ? ((MyProjectileAmmoDefinition)def.AmmoMagazines[0].AmmoDef).ProjectileCount : 1,
                    MagsToLoad = (int)Math.Ceiling((double)(weaponAmmoData.ShotsInBurst == 0 ? 1 : weaponAmmoData.ShotsInBurst) / def.AmmoMagazines[0].AmmoMagDef.Capacity),
                    MaxHeat = 1000,
                    SkipBarrels = 0,
                    ReloadTime = rofOverride != -1 ? (int)(3600f / rofOverride) : weaponAmmoData.ShotsInBurst == 0 ? 0 : (int)(def.WeaponDef.ReloadTime / 1000f * 60),
                    DelayUntilFire = (int)(def.WeaponDef.ShotDelay / 1000f * 60),
                    InventoryFillAmount = 0.95f,
                    InventoryLowAmount = def.BlockDef.InventoryFillFactorMin,
                    UseWorldInventoryVolumeMultiplier = false,
                },
                Audio = new HardPointDef.HardPointAudioDef()
                {
                    PreFiringSound = weaponAmmoData.PreShotSound?.GetCueName() ?? "",
                    FiringSound = firingSound,
                    ReloadSound = "",
                    NoAmmoSound = def.WeaponDef.NoAmmoSound?.GetCueName() ?? "",
                    BarrelRotationSound = def.WeaponDef.SecondarySound?.GetCueName() ?? "",
                    FiringSoundPerShot = soundPerShot,
                    FireSoundEndDelay = (uint)(60f * def.WeaponDef.ReleaseTimeAfterFire / 1000f),
                    FireSoundNoBurst = true,
                },
                Graphics = new HardPointDef.HardPointParticleDef()
                {
                    Effect1 = new ParticleDef
                    {
                        Name = muzzleFlash, // SubtypeId of muzzle particle effect.
                        Offset = muzzleFlashOffset,
                        Extras = new ParticleOptionDef
                        {
                            HitPlayChance = 1f,
                            MaxDistance = 10000f,
                            MaxDuration = def.WeaponDef.MuzzleFlashLifeSpan > 0 ? def.WeaponDef.MuzzleFlashLifeSpan * 60 : def.WeaponDef.ReleaseTimeAfterFire > 0 ? def.WeaponDef.ReleaseTimeAfterFire / 1000f : 1,
                            Scale = 1f,
                            Loop = loopMuzzleFlash,
                            Restart = false, // Whether to end a looping effect instantly when firing stops.
                        },
                    },
                }
            };
        }
        static TargetingDef GetTargetingDef(KeenDef def, WeaponDefinition WCDef)
        {
            var turretDef = def.BlockDef as MyLargeTurretBaseDefinition;

            List<TargetingDef.Threat> threats = new List<TargetingDef.Threat>();

            if (turretDef != null)
            {
                if (!turretDef.HiddenTargetingOptions.HasFlag(Sandbox.Common.ObjectBuilders.Definitions.MyTurretTargetingOptions.Missiles))
                    threats.Add(TargetingDef.Threat.Projectiles); // projectiles first

                if (!turretDef.HiddenTargetingOptions.HasFlag(Sandbox.Common.ObjectBuilders.Definitions.MyTurretTargetingOptions.Players))
                    threats.Add(TargetingDef.Threat.Characters);
                if (!turretDef.HiddenTargetingOptions.HasFlag(Sandbox.Common.ObjectBuilders.Definitions.MyTurretTargetingOptions.LargeShips) || !turretDef.HiddenTargetingOptions.HasFlag(Sandbox.Common.ObjectBuilders.Definitions.MyTurretTargetingOptions.SmallShips))
                    threats.Add(TargetingDef.Threat.Grids);
                if (!turretDef.HiddenTargetingOptions.HasFlag(Sandbox.Common.ObjectBuilders.Definitions.MyTurretTargetingOptions.Neutrals))
                    threats.Add(TargetingDef.Threat.Neutrals);
                if (!turretDef.HiddenTargetingOptions.HasFlag(Sandbox.Common.ObjectBuilders.Definitions.MyTurretTargetingOptions.Asteroids))
                    threats.Add(TargetingDef.Threat.Meteors);
            }
            else
            {
                threats.Add(TargetingDef.Threat.Grids);
                threats.Add(TargetingDef.Threat.Neutrals);
            }

            // no option for friends, enemies, stations (yet)

            float maxAmmoRange = 0f;
            foreach (var a in WCDef.Ammos)
            {
                if (maxAmmoRange < a.Trajectory.MaxTrajectory)
                {
                    maxAmmoRange = a.Trajectory.MaxTrajectory;
                }
            }

            float maxRange = 0;
            if (def.IsTurret && !Session.I.TurretToRange.TryGetValue(turretDef.Id, out maxRange))
            {
                maxRange = turretDef.MaxRangeMeters;
            }

            return new TargetingDef()
            {
                MaxTargetDistance = def.IsTurret ? maxRange > 1 ? Math.Min(maxAmmoRange, maxRange) : maxAmmoRange : maxAmmoRange,
                Threats = threats.ToArray(),
                ClosestFirst = true,
                IgnoreDumbProjectiles = false,
                TopTargets = 4,
                TopBlocks = 250,

                SubSystems = new[]
                {
                    TargetingDef.BlockTypes.Any,
                },
            };
        }
        static ModelAssignmentsDef GetMountPointDef(string subtype, KeenDef def)
        {
            string SpinPart, MuzzlePart, Azi, Elevation, scope;
            List<string> muzzles = new List<string>();

            switch (def.Type)
            {
                case KeenDef.WeaponType.InteriorTurret:
                case KeenDef.WeaponType.Searchlight:
                    SpinPart = "None";
                    MuzzlePart = "InteriorTurretBase2";
                    Azi = "InteriorTurretBase1";
                    Elevation = "InteriorTurretBase2";

                    scope = def.Type == KeenDef.WeaponType.Searchlight ? "spotlight" : "muzzle_projectile";
                    muzzles.Add(scope);
                    break;
                case KeenDef.WeaponType.LargeGatlingTurret:
                    SpinPart = "GatlingBarrel";
                    MuzzlePart = "GatlingBarrel";
                    Azi = "GatlingTurretBase1";
                    Elevation = "GatlingTurretBase2";

                    var tdef = def.BlockDef.GetObjectBuilder() as MyObjectBuilder_LargeTurretBaseDefinition;
                    scope = !string.IsNullOrEmpty(tdef.CameraDummyName) ? tdef.CameraDummyName : "muzzle_projectile";
                    muzzles.Add(!string.IsNullOrEmpty(tdef.MuzzleProjectileDummyName) ? tdef.MuzzleProjectileDummyName : scope);
                    break;
                case KeenDef.WeaponType.LargeMissileTurret:
                    SpinPart = "None";
                    MuzzlePart = "MissileTurretBarrels";
                    Azi = "MissileTurretBase1";
                    Elevation = "MissileTurretBarrels";

                    foreach (var muzzle in def.WeaponDef.WeaponEffects)
                    {
                        if (!muzzles.Contains(muzzle.Dummy))
                            muzzles.Add(muzzle.Dummy);

                    }

                    if (muzzles.Count == 0)
                        muzzles.Add("muzzle_missile_001");

                    var tdef2 = def.BlockDef.GetObjectBuilder() as MyObjectBuilder_LargeTurretBaseDefinition;
                    scope = !string.IsNullOrEmpty(tdef2.CameraDummyName) ? tdef2.CameraDummyName : (muzzles.Count > 0 ? muzzles[0] : "muzzle_missile_001");
                    break;
                case KeenDef.WeaponType.SmallGatlingGun:
                    SpinPart = "Barrel";
                    MuzzlePart = "Barrel";
                    Azi = "None";
                    Elevation = "None";

                    scope = "muzzle_projectile";
                    muzzles.Add(scope);
                    break;
                case KeenDef.WeaponType.SmallMissileLauncherReload:
                case KeenDef.WeaponType.SmallMissileLauncher:
                default:
                    SpinPart = "None";
                    MuzzlePart = "None";
                    Azi = "None";
                    Elevation = "None";

                    foreach (var muzzle in def.WeaponDef.WeaponEffects)
                    {
                        muzzles.Add(muzzle.Dummy);
                    }
                    if (muzzles.Count == 0)
                        muzzles.Add("muzzle_missile_001");

                    scope = muzzles.Count > 0 ? muzzles[0] : "barrel_001";
                    break;
            }

            var iconName = "";

            foreach (var ec in def.EntityComponents)
            {
                if (ec is MyInventoryComponentDefinition)
                {
                    var invdef = ec as MyInventoryComponentDefinition;

                    iconName = !string.IsNullOrEmpty(invdef.InputConstraint.Icon) ? invdef.InputConstraint.Icon : "";
                }

            }

            return new ModelAssignmentsDef()
            {
                MountPoints = new[] {
                    new MountPointDef() {
                        SubtypeId = subtype,
                        SpinPartId = SpinPart,
                        MuzzlePartId = MuzzlePart,
                        AzimuthPartId = Azi,
                        ElevationPartId = Elevation,
                        DurabilityMod = def.BlockDef.GeneralDamageMultiplier,
                        IconName = iconName
                    },
                },
                Muzzles = muzzles.ToArray(),
                Scope = scope,
            };
        }
        public static double AccelTTICalc(double v_o, double v, double a, double d_max)
        {
            if ((a == 0 && v_o == 0) || v == 0)
                return 3600;

            if (a == 0)
                return d_max / v_o;

            var t_max = v / a;

            // d = v_o*t + 1/2at^2
            var d_acc = v_o * t_max + 0.5f * a * t_max * t_max;

            if (d_acc >= d_max)
            {
                // d - v_o*t = 1/2at^2
                // Sqrt(2(d - v_o*t)/a) = t
                // Sqrt(2(d)/a = t^2
                var t_squared = 2 * (d_max - v_o * t_max) / a;
                if (t_squared >= 0)
                    return Math.Sqrt(t_squared);
                else
                    return d_max / v; // bad assumption but better than NaN
            }

            var d_s = d_max - d_acc;
            var t = d_s / v;
            return t_max + t;
        }
        class KeenDef
        {
            public enum WeaponType
            {
                None,
                Searchlight,
                InteriorTurret,
                LargeGatlingTurret,
                LargeMissileTurret,
                SmallGatlingGun,
                SmallMissileLauncher,
                SmallMissileLauncherReload,
            }

            public MyWeaponBlockDefinition BlockDef;
            public WeaponType Type;
            public MyWeaponDefinition WeaponDef;
            public MyContainerDefinition ContainerDef;
            public List<MyComponentDefinitionBase> EntityComponents;
            public List<AmmoMagPair> AmmoMagazines;

            public bool IsTurret => Type == WeaponType.InteriorTurret || Type == WeaponType.LargeGatlingTurret || Type == WeaponType.LargeMissileTurret;
            public bool MissileLauncher => Type == WeaponType.SmallMissileLauncher || Type == WeaponType.SmallMissileLauncherReload || Type == WeaponType.LargeMissileTurret;
            public bool Gatling => Type == WeaponType.SmallGatlingGun || Type == WeaponType.LargeGatlingTurret || Type == WeaponType.InteriorTurret;
            public bool Searchlight => Type == WeaponType.Searchlight;
        }
        class AmmoMagPair
        {
            public MyAmmoMagazineDefinition AmmoMagDef;
            public MyAmmoDefinition AmmoDef;
            public MyPhysicalMaterialDefinition AmmoMaterial;
        }
    }
}
