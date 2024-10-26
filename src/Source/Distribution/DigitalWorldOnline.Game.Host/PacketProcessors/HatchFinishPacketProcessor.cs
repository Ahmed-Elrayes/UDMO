using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchFinishPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchFinish;

        private readonly StatusManager _statusManager;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public HatchFinishPacketProcessor(
            StatusManager statusManager,
            MapServer mapServer,
            AssetsLoader assets,
            ILogger logger,
            ISender sender,
            DungeonsServer dungeonsServer
        )
        {
            _statusManager = statusManager;
            _mapServer = mapServer;
            _assets = assets;
            _logger = logger;
            _sender = sender;
            _dungeonServer = dungeonsServer;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            packet.Skip(5);
            var digiName = packet.ReadString();

            var hatchInfo = _assets.Hatchs.FirstOrDefault(x => x.ItemId == client.Tamer.Incubator.EggId);
            if (hatchInfo == null)
            {
                _logger.Warning($"Unknown hatch info for egg {client.Tamer.Incubator.EggId}.");
                client.Send(new SystemMessagePacket($"Unknown hatch info for egg {client.Tamer.Incubator.EggId}."));
                return;
            }

            byte i = 0;
            while (i < client.Tamer.DigimonSlots)
            {
                if (client.Tamer.Digimons.FirstOrDefault(x => x.Slot == i) == null)
                    break;

                i++;
            }

            var newDigimon = DigimonModel.Create(
                digiName,
                hatchInfo.HatchType,
                hatchInfo.HatchType,
                (DigimonHatchGradeEnum)client.Tamer.Incubator.HatchLevel,
                client.Tamer.Incubator.GetLevelSize(),
                i
            );

            newDigimon.NewLocation(
                client.Tamer.Location.MapId,
                client.Tamer.Location.X,
                client.Tamer.Location.Y
            );

            newDigimon.SetBaseInfo(
                _statusManager.GetDigimonBaseInfo(
                    newDigimon.BaseType
                )
            );

            newDigimon.SetBaseStatus(
                _statusManager.GetDigimonBaseStatus(
                    newDigimon.BaseType,
                    newDigimon.Level,
                    newDigimon.Size
                )
            );

            // Get evolution info
            EvolutionAssetModel digimonEvolutionInfo = _assets.EvolutionInfo.First(x => x.Type == newDigimon.BaseType);

            newDigimon.AddEvolutions(digimonEvolutionInfo);

            if (newDigimon.BaseInfo == null || newDigimon.BaseStatus == null || !newDigimon.Evolutions.Any())
            {
                _logger.Warning($"Unknown digimon info for {newDigimon.BaseType}.");
                client.Send(new SystemMessagePacket($"Unknown digimon info for {newDigimon.BaseType}."));
                return;
            }

            newDigimon.SetTamer(client.Tamer);

            client.Tamer.AddDigimon(newDigimon);

            client.Send(new HatchFinishPacket(newDigimon, (ushort)(client.Partner.GeneralHandler + 1000), client.Tamer.Digimons.FindIndex(x => x == newDigimon)));

            if (client.Tamer.Incubator.PerfectSize(newDigimon.HatchGrade, newDigimon.Size))
            {
                _mapServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name, newDigimon.BaseType, newDigimon.Size).Serialize());
                _dungeonServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name, newDigimon.BaseType, newDigimon.Size).Serialize());
            }

            var digimonInfo = await _sender.Send(new CreateDigimonCommand(newDigimon));

            if (digimonInfo != null)
            {
                newDigimon.SetId(digimonInfo.Id);
                var slot = -1;

                foreach (var digimon in newDigimon.Evolutions)
                {
                    slot++;

                    var evolution = digimonInfo.Evolutions[slot];

                    if (evolution != null)
                    {
                        digimon.SetId(evolution.Id);

                        var skillSlot = -1;

                        foreach (var skill in digimon.Skills)
                        {
                            skillSlot++;

                            var dtoSkill = evolution.Skills[skillSlot];

                            skill.SetId(dtoSkill.Id);
                        }
                    }
                }
            }

            _logger.Verbose($"Character {client.TamerId} hatched {newDigimon.Id}({newDigimon.BaseType}) with grade {newDigimon.HatchGrade} and size {newDigimon.Size}.");

            client.Tamer.Incubator.RemoveEgg();

            // Get digimon base info
            DigimonBaseInfoAssetModel digimonBaseInfo = newDigimon.BaseInfo;

            // Get digimon evolutions
            var digimonEvolutions = newDigimon.Evolutions;

            // Write log with encyclopedia
            _logger.Information($"type: {newDigimon.BaseType}, info: {digimonEvolutionInfo?.Id.ToString()}");

            var encyclopediaExists = client.Tamer.Encyclopedia.Exists(x => x.DigimonEvolutionId == digimonEvolutionInfo?.Id);

            // Check if encyclopedia exists
            if (encyclopediaExists)
            {
                // Write log with encyclopedia
                _logger.Information($"type: {newDigimon.BaseType}, info: {digimonEvolutionInfo?.Id.ToString()}, encyclopedia exists");
            }
            else
            {
                if (digimonEvolutionInfo != null)
                {
                    var encyclopedia = CharacterEncyclopediaModel.Create(client.TamerId, digimonEvolutionInfo.Id, newDigimon.Level, newDigimon.Size, 0, 0, 0, 0, 0, false, false);
                    digimonEvolutions?.ForEach(x =>
                    {
                        var evolutionLine = digimonEvolutionInfo.Lines.FirstOrDefault(y => y.Type == x.Type);
                        byte slotLevel = 0;
                        if (evolutionLine != null)
                        {
                            slotLevel = evolutionLine.SlotLevel;
                        }
                        encyclopedia.Evolutions.Add(CharacterEncyclopediaEvolutionsModel.Create(encyclopedia.Id, x.Type, slotLevel, Convert.ToBoolean(x.Unlocked)));
                    });

                    var encyclopediaAdded = await _sender.Send(new CreateCharacterEncyclopediaCommand(encyclopedia));

                    client.Tamer.Encyclopedia.Add(encyclopediaAdded);

                    _logger.Information($"Tamer encyclopedia count: {client.Tamer.Encyclopedia.Count} and last id is {client.Tamer.Encyclopedia.Last().Id}");
                }
            }

            await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
        }
    }
}