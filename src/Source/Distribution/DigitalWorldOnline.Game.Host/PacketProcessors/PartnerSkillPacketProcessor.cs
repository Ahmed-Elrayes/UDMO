using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Models.Configuration;
using DigitalWorldOnline.GameHost;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Text.Json.Nodes;
using static DigitalWorldOnline.Commons.Packets.GameServer.AddBuffPacket;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public partial class PartnerSkillPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerSkill;

        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IConfiguration _configuration;

        public PartnerSkillPacketProcessor(

            AssetsLoader assets,
            MapServer mapServer,
            ILogger logger, ISender sender,
            DungeonsServer dungeonServer,
            IConfiguration configuration)
        {
            _assets = assets;
            _mapServer = mapServer;
            _logger = logger;
            _sender = sender;
            _dungeonServer = dungeonServer;
            _configuration = configuration;
        }

        public Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var skillSlot = packet.ReadByte();
            var attackerHandler = packet.ReadInt();
            var targetHandler = packet.ReadInt();

            if (client.Partner == null)
                return Task.CompletedTask;

            var skill = _assets.DigimonSkillInfo.FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);

            if (skill == null || skill.SkillInfo == null)
                return Task.CompletedTask;

            var targetSummonMobs = new List<SummonMobModel>();
            SkillTypeEnum skillType;
            var targetPartner = _mapServer.GetEnemyByHandler(client.Tamer.Location.MapId, targetHandler, client.TamerId);

            if (targetPartner != null)
            {
                if (targetPartner.Character.PvpMap == false)
                {
                    client.Partner.StopAutoAttack();

                    client.Send(new SetCombatOffPacket(client.Partner.GeneralHandler).Serialize());
                    //client.Send(new SystemMessagePacket($"Tamer {targetPartner.Name} is not with PVP on"));
                }
            }

            if (client.Tamer.PvpMap && targetPartner.Character.PvpMap && targetPartner != null)
            {
                _logger.Verbose($"Character {client.Tamer.Name} Used a skill {skill.SkillId} in a Player {targetPartner?.Id} - {targetPartner?.Name}.");

                var finalDmg = CalculateDamageOrHealPlayer(client, targetPartner, skill, _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == skill.SkillId), skillSlot) / 15;

                if (finalDmg <= 0) finalDmg = 1;
                if (finalDmg > targetPartner.CurrentHp) finalDmg = targetPartner.CurrentHp;

                var newHp = targetPartner.ReceiveDamage(finalDmg);

                if (newHp > 0)
                {
                    _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in a Player {targetPartner?.Id} - {targetPartner?.Name}.");

                    _mapServer.BroadcastForTamerViewsAndSelf(
                        client.TamerId,
                        new CastSkillPacket(
                            skillSlot,
                            attackerHandler,
                            targetHandler).Serialize());

                    _mapServer.BroadcastForTamerViewsAndSelf(
                        client.TamerId,
                        new SkillHitPacket(
                            attackerHandler,
                            targetPartner.GeneralHandler,
                            skillSlot,
                            finalDmg,
                            targetPartner.HpRate
                            ).Serialize());
                }
                else
                {
                    _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetPartner?.Id} - {targetPartner?.Name} with {finalDmg} skill {skill.Id} damage.");

                    _mapServer.BroadcastForTamerViewsAndSelf(
                        client.TamerId,
                        new KillOnSkillPacket(
                            attackerHandler,
                            targetPartner.GeneralHandler,
                            skillSlot,
                            finalDmg
                            ).Serialize());

                }
            }
            else if (client.DungeonMap)
            {
                var areaOfEffect = skill.SkillInfo.AreaOfEffect;
                var range = skill.SkillInfo.Range;
                var targetType = skill.SkillInfo.Target;

                if (_dungeonServer.GetMobByHandler(client.Tamer.Location.MapId, targetHandler, true, client.TamerId) != null)
                {
                    _logger.Verbose($"Using skill on Summon (Dungeon Server)");

                    var targets = new List<SummonMobModel>();

                    if (areaOfEffect > 0)
                    {
                        skillType = SkillTypeEnum.TargetArea;

                        if (targetType == 17)   // Mobs around partner
                        {
                            targets = _dungeonServer.GetMobsNearbyPartner(client.Partner.Location, areaOfEffect, true, client.TamerId);
                        }
                        else if (targetType == 18)   // Mobs around mob
                        {
                            targets = _dungeonServer.GetMobsNearbyTargetMob(client.Partner.Location.MapId, targetHandler, areaOfEffect, true, client.TamerId);
                        }

                        targetSummonMobs.AddRange(targets);
                    }
                    else
                    {
                        skillType = SkillTypeEnum.Single;

                        var mob = _dungeonServer.GetMobByHandler(client.Tamer.Location.MapId, targetHandler, true, client.TamerId);

                        if (mob == null)
                            return Task.CompletedTask;

                        targetSummonMobs.Add(mob);
                    }

                    _logger.Verbose($"Skill Type: {skillType}");

                    if (targetSummonMobs.Any())
                    {
                        if (skillType == SkillTypeEnum.Single && !targetSummonMobs.First().Alive)
                            return Task.CompletedTask;

                        client.Partner.ReceiveDamage(skill.SkillInfo.HPUsage);
                        client.Partner.UseDs(skill.SkillInfo.DSUsage);

                        var castingTime = (int)Math.Round((float)skill.SkillInfo.CastingTime);
                        if (castingTime <= 0) castingTime = 2000;

                        client.Partner.SetEndCasting(castingTime);

                        targetSummonMobs.ForEach(targetMob =>
                        {
                            _logger.Verbose($"Character {client.Tamer.Id} engaged {targetMob.Id} - {targetMob.Name} with skill {skill.SkillId}.");
                        });

                        if (!client.Tamer.InBattle)
                        {
                            client.Tamer.SetHidden(false);
                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                            client.Tamer.StartBattleWithSkill(targetSummonMobs, skillType);
                        }
                        else
                        {
                            client.Tamer.SetHidden(false);
                            client.Tamer.UpdateTargetWithSkill(targetSummonMobs, skillType);
                        }

                        if (skillType != SkillTypeEnum.Single)
                        {
                            var finalDmg = 0;

                            finalDmg = client.Tamer.GodMode ? targetSummonMobs.First().CurrentHP : SummonAoEDamage(client, targetSummonMobs.First(), skill, skillSlot, _configuration);

                            targetSummonMobs.ForEach(targetMob =>
                            {
                                if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                                if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                                if (!targetMob.InBattle)
                                {
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                                    targetMob.StartBattle(client.Tamer);
                                }
                                else
                                {
                                    targetMob.AddTarget(client.Tamer);
                                }

                                var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                                if (newHp > 0)
                                {
                                    _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in mob {targetMob?.Id} - {targetMob?.Name}.");
                                }
                                else
                                {
                                    _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetMob?.Id} - {targetMob?.Name} with {finalDmg} skill {skill.Id} damage.");
                                    targetMob?.Die();
                                }
                            });

                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize()
                            );

                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new AreaSkillPacket(attackerHandler, client.Partner.HpRate, targetSummonMobs, skillSlot, finalDmg).Serialize()
                            );
                        }
                        else
                        {
                            var targetMob = targetSummonMobs.First();

                            if (!targetMob.InBattle)
                            {
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                                targetMob.StartBattle(client.Tamer);
                            }
                            else
                            {
                                targetMob.AddTarget(client.Tamer);
                            }

                            var finalDmg = client.Tamer.GodMode ? targetMob.CurrentHP : CalculateDamageOrHeal(client, targetMob, skill, _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == skill.SkillId), skillSlot);

                            if (finalDmg <= 0) finalDmg = 1;
                            if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                            var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                            if (newHp > 0)
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in mob {targetMob?.Id} - {targetMob?.Name}.");

                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new CastSkillPacket(
                                        skillSlot,
                                        attackerHandler,
                                        targetHandler).Serialize());

                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new SkillHitPacket(
                                        attackerHandler,
                                        targetMob.GeneralHandler,
                                        skillSlot,
                                        finalDmg,
                                        targetMob.CurrentHpRate
                                        ).Serialize());
                            }
                            else
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetMob?.Id} - {targetMob?.Name} with {finalDmg} skill {skill.Id} damage.");

                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new KillOnSkillPacket(attackerHandler, targetMob.GeneralHandler, skillSlot, finalDmg).Serialize());

                                targetMob?.Die();
                            }
                        }

                        if (!_dungeonServer.MobsAttacking(client.Tamer.Location.MapId, client.TamerId, true))
                        {
                            client.Tamer.StopBattle(true);

                            SendBattleOffTask(client, attackerHandler, true);
                        }

                        var evolution = client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Tamer.Partner.CurrentType);

                        if (evolution != null && skill.SkillInfo.Cooldown / 1000 > 20)
                        {
                            evolution.Skills[skillSlot].SetCooldown(skill.SkillInfo.Cooldown / 1000);
                            _sender.Send(new UpdateEvolutionCommand(evolution));
                        }
                    }
                }
                else
                {
                    _logger.Verbose($"Using skill on Mob (Dungeon Server)");

                    var targetMobs = new List<MobConfigModel>();

                    if (areaOfEffect > 0)
                    {
                        skillType = SkillTypeEnum.TargetArea;

                        var targets = new List<MobConfigModel>();

                        if (targetType == 17)   // Mobs around partner
                        {
                            targets = _dungeonServer.GetMobsNearbyPartner(client.Partner.Location, areaOfEffect, client.TamerId);
                        }
                        else if (targetType == 18)   // Mobs around mob
                        {
                            targets = _dungeonServer.GetMobsNearbyTargetMob(client.Partner.Location.MapId, targetHandler, areaOfEffect, client.TamerId);
                        }

                        targetMobs.AddRange(targets);
                    }
                    else
                    {
                        skillType = SkillTypeEnum.Single;

                        var mob = _dungeonServer.GetMobByHandler(client.Tamer.Location.MapId, targetHandler, client.TamerId);

                        if (mob == null)
                            return Task.CompletedTask;

                        targetMobs.Add(mob);
                    }

                    _logger.Verbose($"Skill Type: {skillType}");

                    if (targetMobs.Any())
                    {
                        if (skillType == SkillTypeEnum.Single && !targetMobs.First().Alive)
                            return Task.CompletedTask;

                        client.Partner.ReceiveDamage(skill.SkillInfo.HPUsage);
                        client.Partner.UseDs(skill.SkillInfo.DSUsage);

                        var castingTime = (int)Math.Round((float)skill.SkillInfo.CastingTime);
                        if (castingTime <= 0) castingTime = 2000;

                        client.Partner.SetEndCasting(castingTime);

                        targetMobs.ForEach(targetMob =>
                        {
                            _logger.Verbose($"Character {client.Tamer.Id} engaged {targetMob.Id} - {targetMob.Name} with skill {skill.SkillId}.");
                        });

                        if (!client.Tamer.InBattle)
                        {
                            client.Tamer.SetHidden(false);
                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                            client.Tamer.StartBattleWithSkill(targetMobs, skillType);
                        }
                        else
                        {
                            client.Tamer.SetHidden(false);
                            client.Tamer.UpdateTargetWithSkill(targetMobs, skillType);
                        }

                        if (skillType != SkillTypeEnum.Single)
                        {
                            var finalDmg = 0;

                            finalDmg = client.Tamer.GodMode ? targetMobs.First().CurrentHP : AoeDamage(client, targetMobs.First(), skill, skillSlot, _configuration);

                            targetMobs.ForEach(targetMob =>
                            {
                                if (finalDmg != 0)
                                {
                                    finalDmg = DebuffReductionDamage(client, finalDmg);
                                }

                                if (finalDmg <= 0) finalDmg = 1;
                                if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                                if (!targetMob.InBattle)
                                {
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                                    targetMob.StartBattle(client.Tamer);
                                }
                                else
                                {
                                    targetMob.AddTarget(client.Tamer);
                                }

                                var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                                if (newHp > 0)
                                {
                                    _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in mob {targetMob?.Id} - {targetMob?.Name}.");
                                }
                                else
                                {
                                    _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetMob?.Id} - {targetMob?.Name} with {finalDmg} skill {skill.Id} damage.");
                                    targetMob?.Die();
                                }
                            });

                            _dungeonServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new CastSkillPacket(
                                    skillSlot,
                                    attackerHandler,
                                    targetHandler
                                ).Serialize()
                            );

                            _dungeonServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new AreaSkillPacket(
                                    attackerHandler,
                                    client.Partner.HpRate,
                                    targetMobs,
                                    skillSlot,
                                    finalDmg
                                ).Serialize()
                            );
                        }
                        else
                        {
                            var targetMob = targetMobs.First();

                            if (!targetMob.InBattle)
                            {
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                                targetMob.StartBattle(client.Tamer);
                            }
                            else
                            {
                                targetMob.AddTarget(client.Tamer);
                            }

                            var finalDmg = client.Tamer.GodMode ? targetMob.CurrentHP : CalculateDamageOrHeal(client, targetMob, skill, _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == skill.SkillId), skillSlot);

                            if (finalDmg != 0 && !client.Tamer.GodMode)
                            {
                                finalDmg = DebuffReductionDamage(client, finalDmg);
                            }

                            if (finalDmg <= 0) finalDmg = 1;
                            if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                            var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                            if (newHp > 0)
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in mob {targetMob?.Id} - {targetMob?.Name}.");

                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new CastSkillPacket(
                                        skillSlot,
                                        attackerHandler,
                                        targetHandler).Serialize());

                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new SkillHitPacket(
                                        attackerHandler,
                                        targetMob.GeneralHandler,
                                        skillSlot,
                                        finalDmg,
                                        targetMob.CurrentHpRate
                                        ).Serialize());


                            }
                            else
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetMob?.Id} - {targetMob?.Name} with {finalDmg} skill {skill.Id} damage.");

                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new KillOnSkillPacket(
                                        attackerHandler,
                                        targetMob.GeneralHandler,
                                        skillSlot,
                                        finalDmg
                                        ).Serialize());

                                targetMob?.Die();
                            }
                        }

                        if (!_dungeonServer.MobsAttacking(client.Tamer.Location.MapId, client.TamerId))
                        {
                            client.Tamer.StopBattle();

                            SendBattleOffTask(client, attackerHandler, true);
                        }

                        var evolution = client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Tamer.Partner.CurrentType);

                        if (evolution != null && skill.SkillInfo.Cooldown / 1000 > 20)
                        {
                            evolution.Skills[skillSlot].SetCooldown(skill.SkillInfo.Cooldown / 1000);
                            _sender.Send(new UpdateEvolutionCommand(evolution));
                        }
                    }
                }
            }
            else
            {
                var areaOfEffect = skill.SkillInfo.AreaOfEffect;
                var range = skill.SkillInfo.Range;
                var targetType = skill.SkillInfo.Target;

                if (_mapServer.GetMobByHandler(client.Tamer.Location.MapId, targetHandler, true, client.TamerId) != null)
                {
                    _logger.Verbose($"Using skill on Summon (Map Server)");

                    var targets = new List<SummonMobModel>();

                    if (areaOfEffect > 0)
                    {
                        skillType = SkillTypeEnum.TargetArea;

                        if (targetType == 17)   // Mobs around partner
                        {
                            targets = _mapServer.GetMobsNearbyPartner(client.Partner.Location, areaOfEffect, true, client.TamerId);
                        }
                        else if (targetType == 18)   // Mobs around mob
                        {
                            targets = _mapServer.GetMobsNearbyTargetMob(client.Partner.Location.MapId, targetHandler, areaOfEffect, true, client.TamerId);
                        }

                        targetSummonMobs.AddRange(targets);
                    }
                    else
                    {
                        skillType = SkillTypeEnum.Single;

                        var mob = _mapServer.GetMobByHandler(client.Tamer.Location.MapId, targetHandler, true, client.TamerId);

                        if (mob == null)
                            return Task.CompletedTask;

                        targetSummonMobs.Add(mob);
                    }

                    _logger.Verbose($"Skill Type: {skillType}");

                    if (targetSummonMobs.Any())
                    {
                        if (skillType == SkillTypeEnum.Single && !targetSummonMobs.First().Alive)
                            return Task.CompletedTask;

                        client.Partner.ReceiveDamage(skill.SkillInfo.HPUsage);
                        client.Partner.UseDs(skill.SkillInfo.DSUsage);

                        var castingTime = (int)Math.Round((float)skill.SkillInfo.CastingTime);
                        if (castingTime <= 0) castingTime = 2000;

                        client.Partner.SetEndCasting(castingTime);

                        targetSummonMobs.ForEach(targetMob =>
                        {
                            _logger.Verbose($"Character {client.Tamer.Id} engaged {targetMob.Id} - {targetMob.Name} with skill {skill.SkillId}.");
                        });

                        if (!client.Tamer.InBattle)
                        {
                            client.Tamer.SetHidden(false);
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                            client.Tamer.StartBattleWithSkill(targetSummonMobs, skillType);
                        }
                        else
                        {
                            client.Tamer.SetHidden(false);
                            client.Tamer.UpdateTargetWithSkill(targetSummonMobs, skillType);
                        }

                        if (skillType != SkillTypeEnum.Single)
                        {
                            var finalDmg = 0;

                            finalDmg = client.Tamer.GodMode ? targetSummonMobs.First().CurrentHP : SummonAoEDamage(client, targetSummonMobs.First(), skill, skillSlot, _configuration);

                            targetSummonMobs.ForEach(targetMob =>
                            {
                                if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                                if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                                if (!targetMob.InBattle)
                                {
                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                                    targetMob.StartBattle(client.Tamer);
                                }
                                else
                                {
                                    targetMob.AddTarget(client.Tamer);
                                }

                                var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                                if (newHp > 0)
                                {
                                    _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in mob {targetMob?.Id} - {targetMob?.Name}.");
                                }
                                else
                                {
                                    _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetMob?.Id} - {targetMob?.Name} with {finalDmg} skill {skill.Id} damage.");
                                    targetMob?.Die();
                                }
                            });

                            _mapServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new CastSkillPacket(
                                    skillSlot,
                                    attackerHandler,
                                    targetHandler
                                ).Serialize()
                            );

                            _mapServer.BroadcastForTamerViewsAndSelf(
                                client.TamerId,
                                new AreaSkillPacket(
                                    attackerHandler,
                                    client.Partner.HpRate,
                                    targetSummonMobs,
                                    skillSlot,
                                    finalDmg
                                ).Serialize()
                            );
                        }
                        else
                        {
                            var targetMob = targetSummonMobs.First();

                            if (!targetMob.InBattle)
                            {
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                                targetMob.StartBattle(client.Tamer);
                            }
                            else
                            {
                                targetMob.AddTarget(client.Tamer);
                            }

                            var finalDmg = client.Tamer.GodMode ? targetMob.CurrentHP : CalculateDamageOrHeal(client, targetMob, skill, _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == skill.SkillId), skillSlot);

                            if (finalDmg != 0 && !client.Tamer.GodMode)
                            {
                                finalDmg = DebuffReductionDamage(client, finalDmg);
                            }

                            if (finalDmg <= 0) finalDmg = 1;
                            if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                            var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                            if (newHp > 0)
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in mob {targetMob?.Id} - {targetMob?.Name}.");

                                _mapServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new CastSkillPacket(
                                        skillSlot,
                                        attackerHandler,
                                        targetHandler).Serialize());

                                _mapServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new SkillHitPacket(
                                        attackerHandler,
                                        targetMob.GeneralHandler,
                                        skillSlot,
                                        finalDmg,
                                        targetMob.CurrentHpRate
                                        ).Serialize());

                            }
                            else
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetMob?.Id} - {targetMob?.Name} with {finalDmg} skill {skill.Id} damage.");

                                _mapServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new KillOnSkillPacket(
                                        attackerHandler,
                                        targetMob.GeneralHandler,
                                        skillSlot,
                                        finalDmg
                                        ).Serialize());

                                targetMob?.Die();
                            }
                        }

                        if (!_mapServer.MobsAttacking(client.Tamer.Location.MapId, client.TamerId, true))
                        {
                            client.Tamer.StopBattle(true);

                            SendBattleOffTask(client, attackerHandler);
                        }

                        var evolution = client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Tamer.Partner.CurrentType);

                        if (evolution != null && skill.SkillInfo.Cooldown / 1000 > 20)
                        {
                            evolution.Skills[skillSlot].SetCooldown(skill.SkillInfo.Cooldown / 1000);
                            _sender.Send(new UpdateEvolutionCommand(evolution));
                        }
                    }
                }
                else
                {
                    _logger.Information($"Using skill on Mob (Map Server)");

                    var targetMobs = new List<MobConfigModel>();

                    if (areaOfEffect > 0)
                    {
                        skillType = SkillTypeEnum.TargetArea;

                        var targets = new List<MobConfigModel>();

                        if (targetType == 17)   // Mobs around partner
                        {
                            _logger.Information($"Target 17");
                            targets = _mapServer.GetMobsNearbyPartner(client.Partner.Location, areaOfEffect, client.TamerId);
                        }
                        else if (targetType == 18)   // Mobs around mob
                        {
                            _logger.Information($"Target 18");
                            targets = _mapServer.GetMobsNearbyTargetMob(client.Partner.Location.MapId, targetHandler, areaOfEffect, client.TamerId);
                        }

                        targetMobs.AddRange(targets);
                    }
                    else if (areaOfEffect == 0 && targetType == 80)
                    {
                        skillType = SkillTypeEnum.Implosion;

                        var targets = new List<MobConfigModel>();

                        targets = _mapServer.GetMobsNearbyTargetMob(client.Partner.Location.MapId, targetHandler, range, client.TamerId);

                        targetMobs.AddRange(targets);
                    }
                    else
                    {
                        skillType = SkillTypeEnum.Single;

                        var mob = _mapServer.GetMobByHandler(client.Tamer.Location.MapId, targetHandler, client.TamerId);

                        if (mob == null)
                            return Task.CompletedTask;

                        targetMobs.Add(mob);
                    }

                    _logger.Information($"Skill Type: {skillType}");

                    if (targetMobs.Any())
                    {
                        if (skillType == SkillTypeEnum.Single && !targetMobs.First().Alive)
                            return Task.CompletedTask;

                        client.Partner.ReceiveDamage(skill.SkillInfo.HPUsage);
                        client.Partner.UseDs(skill.SkillInfo.DSUsage);

                        var castingTime = (int)Math.Round(skill.SkillInfo.CastingTime);
                        if (castingTime <= 0) castingTime = 2000;

                        client.Partner.SetEndCasting(castingTime);

                        targetMobs.ForEach(targetMob =>
                        {
                            _logger.Verbose($"Character {client.Tamer.Id} engaged {targetMob.Id} - {targetMob.Name} with skill {skill.SkillId}.");
                        });

                        if (!client.Tamer.InBattle)
                        {
                            client.Tamer.SetHidden(false);
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(attackerHandler).Serialize());
                            client.Tamer.StartBattleWithSkill(targetMobs, skillType);
                        }
                        else
                        {
                            client.Tamer.SetHidden(false);
                            client.Tamer.UpdateTargetWithSkill(targetMobs, skillType);
                        }

                        if (skillType != SkillTypeEnum.Single)
                        {
                            var finalDmg = client.Tamer.GodMode ? targetMobs.First().CurrentHP : AoeDamage(client, targetMobs.First(), skill, skillSlot, _configuration);

                            targetMobs.ForEach(targetMob =>
                            {
                                if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                                if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                                if (!targetMob.InBattle)
                                {
                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                                    targetMob.StartBattle(client.Tamer);
                                }
                                else
                                {
                                    targetMob.AddTarget(client.Tamer);
                                }

                                var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                                if (newHp > 0)
                                {
                                    _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in mob {targetMob?.Id} - {targetMob?.Name}.");
                                }
                                else
                                {
                                    _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetMob?.Id} - {targetMob?.Name} with {finalDmg} skill {skill.Id} damage.");

                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new KillOnSkillPacket(attackerHandler, targetMob.GeneralHandler, skillSlot, finalDmg).Serialize());

                                    targetMob?.Die();
                                }
                            });

                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize());
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new AreaSkillPacket(attackerHandler, client.Partner.HpRate, targetMobs, skillSlot, finalDmg).Serialize());
                        }
                        else
                        {
                            var targetMob = targetMobs.First();

                            if (!targetMob.InBattle)
                            {
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SetCombatOnPacket(targetHandler).Serialize());
                                targetMob.StartBattle(client.Tamer);
                            }
                            else
                            {
                                targetMob.AddTarget(client.Tamer);
                            }

                            var finalDmg = client.Tamer.GodMode ? targetMob.CurrentHP : CalculateDamageOrHeal(client, targetMob, skill, _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == skill.SkillId), skillSlot);

                            if (finalDmg != 0 && !client.Tamer.GodMode)
                            {
                                finalDmg = DebuffReductionDamage(client, finalDmg);
                            }

                            if (finalDmg <= 0) finalDmg = client.Tamer.Partner.AT;
                            if (finalDmg > targetMob.CurrentHP) finalDmg = targetMob.CurrentHP;

                            var newHp = targetMob.ReceiveDamage(finalDmg, client.TamerId);

                            if (newHp > 0)
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} inflicted {finalDmg} damage with skill {skill.SkillId} in mob {targetMob?.Id} - {targetMob?.Name}.");

                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new CastSkillPacket(skillSlot, attackerHandler, targetHandler).Serialize());

                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new SkillHitPacket(attackerHandler, targetMob.GeneralHandler, skillSlot, finalDmg, targetMob.CurrentHpRate).Serialize());

                                var buffInfo = _assets.BuffInfo.FirstOrDefault(x => x.SkillCode == skill.SkillId || x.DigimonSkillCode == skill.SkillId);

                                if (buffInfo != null)
                                {
                                    foreach (var type in buffInfo.SkillInfo.Apply)
                                    {
                                        switch (type.Attribute)
                                        {
                                            case SkillCodeApplyAttributeEnum.CrowdControl:
                                                {
                                                    var rand = new Random();

                                                    if (UtilitiesFunctions.IncreasePerLevelStun.Contains(skill.SkillId))
                                                    {



                                                        if (type.Chance >= rand.Next(100))
                                                        {
                                                            var duration = UtilitiesFunctions.RemainingTimeSeconds((1 * client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType).Skills[skillSlot].CurrentLevel));

                                                            var newMobDebuff = MobDebuffModel.Create(buffInfo.BuffId, skill.SkillId, 0, (1 * client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType).Skills[skillSlot].CurrentLevel));
                                                            newMobDebuff.SetBuffInfo(buffInfo);

                                                            var activeBuff = targetMob.DebuffList.Buffs.FirstOrDefault(x => x.BuffId == buffInfo.BuffId);

                                                            if (activeBuff != null)
                                                            {
                                                                activeBuff.IncreaseEndDate((1 * client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType).Skills[skillSlot].CurrentLevel));
                                                            }
                                                            else
                                                            {
                                                                targetMob.DebuffList.Buffs.Add(newMobDebuff);
                                                            }

                                                            if (targetMob.CurrentAction != Commons.Enums.Map.MobActionEnum.CrowdControl)
                                                            {
                                                                targetMob.UpdateCurrentAction(Commons.Enums.Map.MobActionEnum.CrowdControl);
                                                            }

                                                            Thread.Sleep(100);

                                                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new AddStunDebuffPacket(targetMob.GeneralHandler, newMobDebuff.BuffId, newMobDebuff.SkillId, duration).Serialize());
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (type.Chance >= rand.Next(100))
                                                        {
                                                            var duration = UtilitiesFunctions.RemainingTimeSeconds(skill.TimeForCrowdControl());

                                                            var newMobDebuff = MobDebuffModel.Create(buffInfo.BuffId, skill.SkillId, 0, skill.TimeForCrowdControl());
                                                            newMobDebuff.SetBuffInfo(buffInfo);

                                                            var activeBuff = targetMob.DebuffList.Buffs.FirstOrDefault(x => x.BuffId == buffInfo.BuffId);

                                                            if (activeBuff != null)
                                                            {
                                                                activeBuff.IncreaseEndDate(skill.TimeForCrowdControl());
                                                            }
                                                            else
                                                            {
                                                                targetMob.DebuffList.Buffs.Add(newMobDebuff);
                                                            }

                                                            if (targetMob.CurrentAction != Commons.Enums.Map.MobActionEnum.CrowdControl)
                                                            {
                                                                targetMob.UpdateCurrentAction(Commons.Enums.Map.MobActionEnum.CrowdControl);
                                                            }

                                                            Thread.Sleep(100);

                                                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new AddStunDebuffPacket(targetMob.GeneralHandler, newMobDebuff.BuffId, newMobDebuff.SkillId, duration).Serialize());
                                                        }

                                                    }
                                                }
                                                break;
                                        }
                                    }
                                }

                            }
                            else
                            {
                                _logger.Verbose($"Partner {client.Partner.Id} killed mob {targetMob?.Id} - {targetMob?.Name} with {finalDmg} skill {skill.Id} damage.");

                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new KillOnSkillPacket(
                                        attackerHandler, targetMob.GeneralHandler, skillSlot, finalDmg).Serialize());

                                targetMob?.Die();
                            }

                        }

                        if (!_mapServer.MobsAttacking(client.Tamer.Location.MapId, client.TamerId))
                        {
                            client.Tamer.StopBattle();

                            SendBattleOffTask(client, attackerHandler);
                        }

                        var evolution = client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Tamer.Partner.CurrentType);

                        // Save cooldown in database if the cooldown is more than 20 seconds
                        if (evolution != null && skill.SkillInfo.Cooldown / 1000 >= 20)
                        {
                            evolution.Skills[skillSlot].SetCooldown(skill.SkillInfo.Cooldown / 1000);
                            _sender.Send(new UpdateEvolutionCommand(evolution));
                        }
                    }
                }
            }

            return Task.CompletedTask;
        }

        // -------------------------------------------------------------------------------------

        public async Task SendBattleOffTask(GameClient client, int attackerHandler)
        {
            await Task.Run(async () =>
            {
                Thread.Sleep(4000);

                _mapServer.BroadcastForTamerViewsAndSelf(
                        client.TamerId,
                        new SetCombatOffPacket(attackerHandler).Serialize()
                    );
            });
        }

        public async Task SendBattleOffTask(GameClient client, int attackerHandler, bool dungeon)
        {
            await Task.Run(async () =>
            {
                Thread.Sleep(4000);

                _dungeonServer.BroadcastForTamerViewsAndSelf(
                        client.TamerId,
                        new SetCombatOffPacket(attackerHandler).Serialize()
                    );
            });
        }

        // -------------------------------------------------------------------------------------

        private static int DebuffReductionDamage(GameClient client, int finalDmg)
        {
            if (client.Tamer.Partner.DebuffList.ActiveDebuffReductionDamage())
            {
                var debuffInfo = client.Tamer.Partner.DebuffList.ActiveBuffs.Where(buff => buff.BuffInfo.SkillInfo.Apply.Any(apply => apply.Attribute == Commons.Enums.SkillCodeApplyAttributeEnum.AttackPowerDown)).ToList();

                var totalValue = 0;
                var SomaValue = 0;

                foreach (var debuff in debuffInfo)
                {
                    foreach (var apply in debuff.BuffInfo.SkillInfo.Apply)
                    {

                        switch (apply.Type)
                        {
                            case SkillCodeApplyTypeEnum.Default:
                                totalValue += apply.Value;
                                break;

                            case SkillCodeApplyTypeEnum.AlsoPercent:
                            case SkillCodeApplyTypeEnum.Percent:
                                {

                                    SomaValue += apply.Value + (debuff.TypeN) * apply.IncreaseValue;

                                    double fatorReducao = SomaValue / 100;

                                    // Calculando o novo finalDmg após a redução
                                    finalDmg -= (int)(finalDmg * fatorReducao);

                                }
                                break;

                            case SkillCodeApplyTypeEnum.Unknown200:
                                {

                                    SomaValue += apply.AdditionalValue;

                                    double fatorReducao = SomaValue / 100.0;

                                    // Calculando o novo finalDmg após a redução
                                    finalDmg -= (int)(finalDmg * fatorReducao);

                                }
                                break;

                        }
                        break;

                    }
                }
            }

            return finalDmg;
        }

        // -------------------------------------------------------------------------------------

        private int CalculateDamageOrHeal(GameClient client, MobConfigModel? targetMob, DigimonSkillAssetModel? targetSkill, SkillCodeAssetModel? skill, byte skillSlot)
        {
            var SkillValue = skill.Apply.FirstOrDefault(x => x.Type > 0);

            double f1BaseDamage = (SkillValue.Value) + ((client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType).Skills[skillSlot].CurrentLevel) * SkillValue.IncreaseValue);

            double SkillFactor = 0;
            int clonDamage = 0;
            var attributeMultiplier = 0.00;
            var elementMultiplier = 0.00;

            var Percentual = (decimal)client.Partner.SCD / 100;
            SkillFactor = (double)Percentual;

            // -- CLON -------------------------------------------------------------------

            double clonPercent = client.Tamer.Partner.Digiclone.ATValue / 100.0;
            int cloValue = (int)(client.Tamer.Partner.BaseStatus.ATValue * clonPercent);

            double factorFromPF = 144.0 / client.Tamer.Partner.Digiclone.ATValue;
            double cloneFactor = Math.Round(1.0 + (0.43 / factorFromPF), 2);

            // ---------------------------------------------------------------------------

            f1BaseDamage = Math.Floor(f1BaseDamage * cloneFactor);

            double addedf1Damage = Math.Floor(f1BaseDamage * SkillFactor / 100.0);

            // ---------------------------------------------------------------------------

            int baseDamage = (int)Math.Floor(f1BaseDamage + addedf1Damage + client.Tamer.Partner.AT + client.Tamer.Partner.SKD);

            // clon Verification
            if (client.Tamer.Partner.Digiclone.ATLevel > 0)
                clonDamage = (int)(f1BaseDamage * 0.301);
            else
                clonDamage = 0;

            // Attribute
            if (client.Tamer.Partner.BaseInfo.Attribute.HasAttributeAdvantage(targetMob.Attribute))
            {
                var attExp = client.Tamer.Partner.GetAttributeExperience();
                var attValue = client.Partner.ATT / 100.0;
                var attValuePercent = attValue / 100.0;
                var bonusMax = 1;
                var expMax = 10000;

                attributeMultiplier = ((bonusMax + attValuePercent) * attExp) / expMax;
            }
            else if (targetMob.Attribute.HasAttributeAdvantage(client.Tamer.Partner.BaseInfo.Attribute))
            {
                attributeMultiplier -= 0.25;
            }

            // Element
            if (client.Tamer.Partner.BaseInfo.Element.HasElementAdvantage(targetMob.Element))
            {
                var elementValue = client.Tamer.Partner.GetElementExperience();
                var bonusMax = 1;
                var expMax = 10000;

                elementMultiplier = (bonusMax * elementValue) / expMax;
            }
            else if (targetMob.Element.HasElementAdvantage(client.Tamer.Partner.BaseInfo.Element))
            {
                elementMultiplier -= 0.25;
            }

            // ---------------------------------------------------------------------------

            int attributeBonus = (int)Math.Floor(f1BaseDamage * attributeMultiplier);
            int elementBonus = (int)Math.Floor(f1BaseDamage * elementMultiplier);

            int totalDamage = baseDamage + clonDamage + attributeBonus + elementBonus;

            //_logger.Information($"Skill Damage: {f1BaseDamage} | ClonDamage: {clonDamage}");
            //_logger.Information($"Partner.AT: {client.Tamer.Partner.AT} | Partner.SKD: {client.Tamer.Partner.SKD} | Att Damage: {addedf1Damage}");
            //_logger.Information($"Attribute Damage: {(attributeMultiplier * f1BaseDamage)} | Element Damage: {(elementMultiplier * f1BaseDamage)}");
            //_logger.Information($"Total Single Damage: {totalDamage}\n");

            return totalDamage;
        }

        private int CalculateDamageOrHeal(GameClient client, SummonMobModel? targetMob, DigimonSkillAssetModel? targetSkill, SkillCodeAssetModel? skill, byte skillSlot)
        {
            var SkillValue = skill.Apply.FirstOrDefault(x => x.Type > 0);

            double f1BaseDamage = (SkillValue.Value) + ((client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType).Skills[skillSlot].CurrentLevel) * SkillValue.IncreaseValue);

            double SkillFactor = 0;
            int clonDamage = 0;
            var attributeMultiplier = 0.00;
            var elementMultiplier = 0.00;

            var Percentual = (decimal)client.Partner.SCD / 100;
            SkillFactor = (double)Percentual;

            // -- CLON -------------------------------------------------------------------

            double clonPercent = client.Tamer.Partner.Digiclone.ATValue / 100.0;
            int cloValue = (int)(client.Tamer.Partner.BaseStatus.ATValue * clonPercent);

            double factorFromPF = 144.0 / client.Tamer.Partner.Digiclone.ATValue;
            double cloneFactor = Math.Round(1.0 + (0.43 / factorFromPF), 2);

            // ---------------------------------------------------------------------------

            f1BaseDamage = Math.Floor(f1BaseDamage * cloneFactor);

            double addedf1Damage = Math.Floor(f1BaseDamage * SkillFactor / 100.0);

            // ---------------------------------------------------------------------------

            int baseDamage = (int)Math.Floor(f1BaseDamage + addedf1Damage + client.Tamer.Partner.AT + client.Tamer.Partner.SKD);

            // clon Verification
            if (client.Tamer.Partner.Digiclone.ATLevel > 0)
                clonDamage = (int)(f1BaseDamage * 0.301);
            else
                clonDamage = 0;

            // Attribute
            if (client.Tamer.Partner.BaseInfo.Attribute.HasAttributeAdvantage(targetMob.Attribute))
            {
                var attExp = client.Tamer.Partner.GetAttributeExperience();
                var attValue = client.Partner.ATT / 100.0;
                var attValuePercent = attValue / 100.0;
                var bonusMax = 1;
                var expMax = 10000;

                attributeMultiplier = ((bonusMax + attValuePercent) * attExp) / expMax;
            }
            else if (targetMob.Attribute.HasAttributeAdvantage(client.Tamer.Partner.BaseInfo.Attribute))
            {
                attributeMultiplier = -0.25;
            }

            // Element
            if (client.Tamer.Partner.BaseInfo.Element.HasElementAdvantage(targetMob.Element))
            {
                var elementValue = client.Tamer.Partner.GetElementExperience();
                var bonusMax = 1;
                var expMax = 10000;

                elementMultiplier = (bonusMax * elementValue) / expMax;
            }
            else if (targetMob.Element.HasElementAdvantage(client.Tamer.Partner.BaseInfo.Element))
            {
                elementMultiplier = -0.25;
            }

            // ---------------------------------------------------------------------------

            int attributeBonus = (int)Math.Floor(f1BaseDamage * attributeMultiplier);
            int elementBonus = (int)Math.Floor(f1BaseDamage * elementMultiplier);

            int totalDamage = baseDamage + clonDamage + attributeBonus + elementBonus;

            return totalDamage;
        }

        private int CalculateDamageOrHealPlayer(GameClient client, DigimonModel? targetMob, DigimonSkillAssetModel? targetSkill, SkillCodeAssetModel? skill, byte skillSlot)
        {

            var SkillValue = skill.Apply.FirstOrDefault(x => x.Type > 0);

            double f1BaseDamage = (SkillValue.Value) + ((client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType).Skills[skillSlot].CurrentLevel) * SkillValue.IncreaseValue);
            double SkillFactor = 0;
            double MultiplierAttribute = 0;

            var Percentual = (decimal)client.Partner.SCD / 100;
            SkillFactor = (double)Percentual; // AttributeMultiplier

            double factorFromPF = 144.0 / client.Tamer.Partner.Digiclone.ATValue;

            double cloneFactor = Math.Round(1.0 + (0.43 / factorFromPF), 2);
            f1BaseDamage = Math.Floor(f1BaseDamage * cloneFactor);

            double addedf1Damage = Math.Floor(f1BaseDamage * SkillFactor / 100.0);

            var attributeVantage = client.Tamer.Partner.BaseInfo.Attribute.HasAttributeAdvantage(targetMob.BaseInfo.Attribute);
            var elementVantage = client.Tamer.Partner.BaseInfo.Element.HasElementAdvantage(targetMob.BaseInfo.Element);

            var Damage = (int)Math.Floor(f1BaseDamage + addedf1Damage + (client.Tamer.Partner.AT / targetMob.DE) + client.Tamer.Partner.SKD);

            if (client.Partner.AttributeExperience.CurrentAttributeExperience && attributeVantage)
            {

                MultiplierAttribute = (2 + ((client.Partner.ATT) / 200.0));
                Random random = new Random();

                // Gere um valor aleatório entre 0% e 5% a mais do valor original
                double percentagemBonus = random.NextDouble() * 0.05;

                // Calcule o valor final com o bônus
                return (int)((int)Math.Floor(MultiplierAttribute * Damage) * (1.0 + percentagemBonus));

            }
            else if (client.Partner.AttributeExperience.CurrentElementExperience && elementVantage)
            {
                MultiplierAttribute = 2;

                Random random = new Random();

                // Gere um valor aleatório entre 0% e 5% a mais do valor original
                double percentagemBonus = random.NextDouble() * 0.05;

                // Calcule o valor final com o bônus
                return (int)((int)Math.Floor(MultiplierAttribute * Damage) * (1.0 + percentagemBonus));
            }
            else
            {
                Random random = new Random();

                // Gere um valor aleatório entre 0% e 5% a mais do valor original
                double percentagemBonus = random.NextDouble() * 0.05;

                // Calcule o valor final com o bônus
                return (int)(Damage * (1.0 + percentagemBonus));


            }

        }

        // -------------------------------------------------------------------------------------

        private int AoeDamage(GameClient client, MobConfigModel? targetMob, DigimonSkillAssetModel? targetSkill, byte skillSlot, IConfiguration configuration)
        {
            var skillCode = _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == targetSkill.SkillId);
            var skill = _assets.DigimonSkillInfo.FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            var skillValue = skillCode?.Apply.FirstOrDefault(x => x.Type > 0);

            double f1BaseDamage = skillValue.Value + (client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType).Skills[skillSlot].CurrentLevel * skillValue.IncreaseValue);

            double SkillFactor = 0;
            int clonDamage = 0;
            var attributeMultiplier = 0.00;
            var elementMultiplier = 0.00;

            var Percentual = (decimal)client.Partner.SCD / 100;
            SkillFactor = (double)Percentual;

            // -- CLON -------------------------------------------------------------------

            double factorFromPF = 144.0 / client.Tamer.Partner.Digiclone.ATValue;
            double cloneFactor = Math.Round(1.0 + (0.43 / factorFromPF), 2);

            f1BaseDamage = Math.Floor(f1BaseDamage * cloneFactor);

            double addedf1Damage = Math.Floor(f1BaseDamage * SkillFactor / 100.0);

            // ---------------------------------------------------------------------------

            int baseDamage = (int)Math.Floor(f1BaseDamage + addedf1Damage + client.Tamer.Partner.AT + client.Tamer.Partner.SKD);

            // clon Verification
            if (client.Tamer.Partner.Digiclone.ATLevel > 0)
                clonDamage = (int)(f1BaseDamage * 0.301);
            else
                clonDamage = 0;

            // Attribute
            if (client.Tamer.Partner.BaseInfo.Attribute.HasAttributeAdvantage(targetMob.Attribute))
            {
                var attExp = client.Tamer.Partner.GetAttributeExperience();
                var attValue = client.Partner.ATT / 100.0;
                var attValuePercent = attValue / 100.0;
                var bonusMax = 1;
                var expMax = 10000;

                attributeMultiplier = ((bonusMax + attValuePercent) * attExp) / expMax;
            }
            else if (targetMob.Attribute.HasAttributeAdvantage(client.Tamer.Partner.BaseInfo.Attribute))
            {
                attributeMultiplier = -0.25;
            }

            // Element
            if (client.Tamer.Partner.BaseInfo.Element.HasElementAdvantage(targetMob.Element))
            {
                var elementValue = client.Tamer.Partner.GetElementExperience();
                var bonusMax = 1;
                var expMax = 10000;

                elementMultiplier = (bonusMax * elementValue) / expMax;
            }
            else if (targetMob.Element.HasElementAdvantage(client.Tamer.Partner.BaseInfo.Element))
            {
                elementMultiplier = -0.25;
            }

            // ---------------------------------------------------------------------------

            int attributeBonus = (int)Math.Floor(f1BaseDamage * attributeMultiplier);
            int elementBonus = (int)Math.Floor(f1BaseDamage * elementMultiplier);

            int totalDamage = baseDamage + clonDamage + attributeBonus + elementBonus;

            //_logger.Information($"Skill Damage: {f1BaseDamage} | Att Damage: {addedf1Damage} | Clon Damage: {clonDamage}");
            //_logger.Information($"Partner.AT: {client.Tamer.Partner.AT} | Partner.SKD: {client.Tamer.Partner.SKD}");
            //_logger.Information($"Attribute Damage: {attributeBonus} | Element Damage: {elementBonus}");
            //_logger.Information($"Total Area Damage: {totalDamage}\n");

            return totalDamage;
        }

        private int SummonAoEDamage(GameClient client, SummonMobModel? targetMob, DigimonSkillAssetModel? targetSkill, byte skillSlot, IConfiguration configuration)
        {
            var skillCode = _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == targetSkill.SkillId);
            var skill = _assets.DigimonSkillInfo.FirstOrDefault(x => x.Type == client.Partner.CurrentType && x.Slot == skillSlot);
            var skillValue = skillCode?.Apply.FirstOrDefault(x => x.Type > 0);

            double f1BaseDamage = skillValue.Value + (client.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Partner.CurrentType).Skills[skillSlot].CurrentLevel * skillValue.IncreaseValue);

            double SkillFactor = 0;
            int clonDamage = 0;
            var attributeMultiplier = 0.00;
            var elementMultiplier = 0.00;

            var Percentual = (decimal)client.Partner.SCD / 100;
            SkillFactor = (double)Percentual;

            // -- CLON -------------------------------------------------------------------

            double factorFromPF = 144.0 / client.Tamer.Partner.Digiclone.ATValue;
            double cloneFactor = Math.Round(1.0 + (0.43 / factorFromPF), 2);

            f1BaseDamage = Math.Floor(f1BaseDamage * cloneFactor);

            double addedf1Damage = Math.Floor(f1BaseDamage * SkillFactor / 100.0);

            // ---------------------------------------------------------------------------

            int baseDamage = (int)Math.Floor(f1BaseDamage + addedf1Damage + client.Tamer.Partner.AT + client.Tamer.Partner.SKD);

            // clon Verification
            if (client.Tamer.Partner.Digiclone.ATLevel > 0)
                clonDamage = (int)(f1BaseDamage * 0.301);
            else
                clonDamage = 0;

            // Attribute
            if (client.Tamer.Partner.BaseInfo.Attribute.HasAttributeAdvantage(targetMob.Attribute))
            {
                var attExp = client.Tamer.Partner.GetAttributeExperience();
                var attValue = client.Partner.ATT / 100.0;
                var attValuePercent = attValue / 100.0;
                var bonusMax = 1;
                var expMax = 10000;

                attributeMultiplier = ((bonusMax + attValuePercent) * attExp) / expMax;
            }
            else if (targetMob.Attribute.HasAttributeAdvantage(client.Tamer.Partner.BaseInfo.Attribute))
            {
                attributeMultiplier = -0.25;
            }

            // Element
            if (client.Tamer.Partner.BaseInfo.Element.HasElementAdvantage(targetMob.Element))
            {
                var elementValue = client.Tamer.Partner.GetElementExperience();
                var bonusMax = 1;
                var expMax = 10000;

                elementMultiplier = (bonusMax * elementValue) / expMax;
            }
            else if (targetMob.Element.HasElementAdvantage(client.Tamer.Partner.BaseInfo.Element))
            {
                elementMultiplier = -0.25;
            }

            // ---------------------------------------------------------------------------

            int attributeBonus = (int)Math.Floor(f1BaseDamage * attributeMultiplier);
            int elementBonus = (int)Math.Floor(f1BaseDamage * elementMultiplier);

            int totalDamage = baseDamage + clonDamage + attributeBonus + elementBonus;

            return totalDamage;
        }

        // -------------------------------------------------------------------------------------

    }
}