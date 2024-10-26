using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Writers;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class EncyclopediaDeckBuffUsePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.EncyclopediaDeckBuffUse;

        private readonly ISender _sender;
        private readonly ILogger _logger;

        public EncyclopediaDeckBuffUsePacketProcessor(ISender sender, ILogger logger)
        {
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            int deckBuffId = packet.ReadInt();

            var encyclopedia = client.Tamer.Encyclopedia;

            _logger.Information($"Getting encyclopedia data");

            var character = client.Tamer;

            character.UpdateDeckBuffId(deckBuffId == 0 ? null : deckBuffId);

            await _sender.Send(new UpdateCharacterDeckBuffCommand(character));

            client.Send(new EncyclopediaDeckBuffUsePacket(character));
        }
    }
}