using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Writers;
using MediatR;
using Serilog;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class EncyclopediaGetRewardPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.EncyclopediaGetReward;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public EncyclopediaGetRewardPacketProcessor(AssetsLoader assets, ISender sender, ILogger logger)
        {
            _assets = assets;
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            // Get digimon id
            var digimonId = packet.ReadUInt();

            var evoInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == digimonId);
            if (evoInfo == null)
            {
                client.Send(new SystemMessagePacket($"Failed to receive reward."));
                return;
            }

            var encyclopedia = client.Tamer.Encyclopedia.FirstOrDefault(x => x.DigimonEvolutionId == evoInfo.Id);

            if (encyclopedia == null)
            {
                _logger.Warning($"Failed to send encyclopedia reward to tamer {client.TamerId}, Player does not have this opened.");
                client.Send(new SystemMessagePacket($"Failed to receive reward."));
                return;
            }

            if (encyclopedia.IsRewardReceived)
            {
                _logger.Warning($"Tamer {client.TamerId}, Already received the reward.");
                client.Send(new SystemMessagePacket($"You have already received the reward."));
                return;
            }

            if (!encyclopedia.IsRewardAllowed)
            {
                _logger.Warning($"Tamer {client.TamerId}, Is not allowed to take the item.");
                client.Send(new SystemMessagePacket($"Failed to receive reward."));
                return;
            }

            var itemId = 97206;
            var newItem = new ItemModel();
            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

            if (newItem.ItemInfo == null)
            {
                _logger.Warning($"Failed to send encyclopedia reward to tamer {client.TamerId}.");
                client.Send(new SystemMessagePacket($"Failed to receive reward."));
                return;
            }

            newItem.ItemId = itemId;
            newItem.Amount = 10;

            if (newItem.IsTemporary)
                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

            var itemClone = (ItemModel)newItem.Clone();
            if (client.Tamer.Inventory.AddItem(newItem))
            {
                encyclopedia.SetRewardAllowed(false);
                encyclopedia.SetRewardReceived(true);
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateCharacterEncyclopediaCommand(encyclopedia));
                client.Send(new EncyclopediaReceiveRewardItemPacket(newItem, (int)digimonId));
            }
            else
            {
                client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
            }
        }
    }
}