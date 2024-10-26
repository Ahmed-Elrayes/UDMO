using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using MediatR;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class InitialInformationPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.InitialInformation;

        private readonly PartyManager _partyManager;
        private readonly StatusManager _statusManager;
        private readonly MapServer _mapServer;
        private readonly PvpServer _pvpServer;
        private readonly DungeonsServer _dungeonsServer;

        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;

        public InitialInformationPacketProcessor(
            PartyManager partyManager,
            StatusManager statusManager,
            MapServer mapServer,
            PvpServer pvpServer,
            DungeonsServer dungeonsServer,
            AssetsLoader assets,
            ILogger logger,
            ISender sender,
            IMapper mapper)
        {
            _partyManager = partyManager;
            _statusManager = statusManager;
            _mapServer = mapServer;
            _pvpServer = pvpServer;
            _dungeonsServer = dungeonsServer;
            _assets = assets;
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            packet.Skip(4);
            var accountId = packet.ReadUInt();
            var accessCode = packet.ReadUInt();
            
            var account = _mapper.Map<AccountModel>(await _sender.Send(new AccountByIdQuery(accountId)));
            client.SetAccountInfo(account);

            try
            {
                var character = _mapper.Map<CharacterModel>(await _sender.Send(new CharacterByIdQuery(account.LastPlayedCharacter)));

                _logger.Information($"Search character with id {account.LastPlayedCharacter} for account {account.Id}...");

                if (character.Partner == null)
                {
                    _logger.Warning($"Invalid character information for tamer id {account.LastPlayedCharacter}.");
                    return;
                }

                account.ItemList.ForEach(character.AddItemList);

                foreach (var digimon in character.Digimons)
                {
                    digimon.SetTamer(character);

                    digimon.SetBaseInfo(_statusManager.GetDigimonBaseInfo(digimon.CurrentType));

                    digimon.SetBaseStatus(_statusManager.GetDigimonBaseStatus(digimon.CurrentType, digimon.Level, digimon.Size));

                    digimon.SetTitleStatus(_statusManager.GetTitleStatus(character.CurrentTitle));

                    digimon.SetSealStatus(_assets.SealInfo);
                }

                character.SetBaseStatus(_statusManager.GetTamerBaseStatus(character.Model));
                character.SetLevelStatus(_statusManager.GetTamerLevelStatus(character.Model, character.Level));

                character.NewViewLocation(character.Location.X, character.Location.Y);
                character.NewLocation(character.Location.MapId, character.Location.X, character.Location.Y);
                character.Partner.NewLocation(character.Location.MapId, character.Location.X, character.Location.Y);

                character.RemovePartnerPassiveBuff();
                character.SetPartnerPassiveBuff();

                await _sender.Send(new UpdateDigimonBuffListCommand(character.Partner.BuffList));

                foreach (var item in character.ItemList.SelectMany(x => x.Items).Where(x => x.ItemId > 0))
                    item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item?.ItemId));

                foreach (var buff in character.BuffList.ActiveBuffs)
                    buff.SetBuffInfo(_assets.BuffInfo.FirstOrDefault(x => x.SkillCode == buff.SkillId || x.DigimonSkillCode == buff.SkillId));

                foreach (var buff in character.Partner.BuffList.ActiveBuffs)
                    buff.SetBuffInfo(_assets.BuffInfo.FirstOrDefault(x => x.SkillCode == buff.SkillId || x.DigimonSkillCode == buff.SkillId));

                _logger.Debug($"Getting available channels...");

                var channels = await _sender.Send(new ChannelsByMapIdQuery(character.Location.MapId));

                byte? channel;

                if (character.Channel == byte.MaxValue && !channels.IsNullOrEmpty())
                {
                    Random random = new Random();
                    List<byte> keys = new List<byte>(channels.Keys);

                    channel = keys[random.Next(keys.Count)];
                }
                else
                {
                    channel = character.Channel;
                }

                if (channel == null)
                {
                    _logger.Debug($"Creating new channel for map {character.Location.MapId}...");
                    channels.Add(channels.Keys.GetNewChannel(), 1);
                    channel = channels.OrderByDescending(x => x.Value).FirstOrDefault(x => x.Value < byte.MaxValue).Key;
                }
                
                if (character.Channel == 255)
                {
                    character.SetCurrentChannel(channel);
                }

                character.UpdateState(CharacterStateEnum.Loading);
                client.SetCharacter(character);
                client.SetSentOnceDataSent(character.InitialPacketSentOnceSent);

                _logger.Debug($"Updating character state...");
                await _sender.Send(new UpdateCharacterStateCommand(character.Id, CharacterStateEnum.Loading));

                if (client.DungeonMap)
                {
                    client.Tamer.SetCurrentChannel(0);
                    _dungeonsServer.AddClient(client);
                    _logger.Information($"Adding character {character.Name}({character.Id}) to map {character.Location.MapId} {character.GeneralHandler} - {character.Partner.GeneralHandler}...");
                }
                else
                {
                    _mapServer.AddClient(client);
                    _logger.Information($"Adding character {character.Name}({character.Id}) to map {character.Location.MapId} {character.GeneralHandler} - {character.Partner.GeneralHandler} on Channel {character.Channel}...");
                }

                while (client.Loading) await Task.Delay(1000);

                character.SetGenericHandler(character.Partner.GeneralHandler);

                var party = _partyManager.FindParty(client.TamerId);

                if (party != null)
                {
                    party.UpdateMember(party[client.TamerId], character);

                    foreach (var target in party.Members.Values)
                    {
                        var targetClient = _mapServer.FindClientByTamerId(target.Id);

                        if (targetClient == null) targetClient = _dungeonsServer.FindClientByTamerId(target.Id);

                        if (targetClient == null) continue;

                        if (target.Id != client.TamerId)
                        {
                            targetClient.Send(
                                UtilitiesFunctions.GroupPackets(
                                new PartyMemberWarpGatePacket(party[client.TamerId]).Serialize(),
                                new PartyMemberMovimentationPacket(party[client.TamerId]).Serialize()
                            ));
                        }
                    }

                    client.Send(new PartyMemberListPacket(party, client.TamerId, (byte)(party.Members.Count - 1)));
                }

                if (!client.DungeonMap)
                {
                    var region = _assets.Maps.FirstOrDefault(x => x.MapId == character.Location.MapId);

                    if (region != null)
                    {
                        if (character.MapRegions[region.RegionIndex].Unlocked != 0x80)
                        {
                            var characterRegion = character.MapRegions[region.RegionIndex];
                            characterRegion.Unlock();

                            await _sender.Send(new UpdateCharacterMapRegionCommand(characterRegion));
                        }
                    }
                }

                await ReceiveArenaPoints(client);

                client.Send(new InitialInfoPacket(character, party));

                await _sender.Send(new ChangeTamerIdTPCommand(client.Tamer.Id, (int)0));

                _logger.Debug($"Updating character channel...");
                await _sender.Send(new UpdateCharacterChannelCommand(character.Id, character.Channel));

            }
            catch (Exception ex)
            {
                _logger.Error($"[{account.LastPlayedCharacter}] An error occurred: {ex.Message}, Line: {ex.Source.ToString()}, Stacktrace: {ex.StackTrace.ToString()}", ex);
                client.Disconnect();
            }
            finally
            {
            }
        }

        private async Task ReceiveArenaPoints(GameClient client)
        {
            if (client.Tamer.Points.Amount > 0)
            {
                var newItem = new ItemModel();
                newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == client.Tamer.Points.ItemId));

                newItem.ItemId = client.Tamer.Points.ItemId;
                newItem.Amount = client.Tamer.Points.Amount;

                if (newItem.IsTemporary)
                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                var itemClone = (ItemModel)newItem.Clone();

                if (client.Tamer.Inventory.AddItem(newItem))
                {
                    await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                }
                else
                {
                    newItem.EndDate = DateTime.Now.AddDays(7);

                    client.Tamer.GiftWarehouse.AddItem(newItem);
                    await _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
                }

                client.Tamer.Points.SetAmount(0);
                client.Tamer.Points.SetCurrentStage(0);

                await _sender.Send(new UpdateCharacterArenaPointsCommand(client.Tamer.Points));
            }
            else if (client.Tamer.Points.CurrentStage > 0)
            {
                client.Tamer.Points.SetCurrentStage(0);
                await _sender.Send(new UpdateCharacterArenaPointsCommand(client.Tamer.Points));
            }
        }
    }
}
