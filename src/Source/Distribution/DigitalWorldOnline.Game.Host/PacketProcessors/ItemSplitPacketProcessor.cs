﻿using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ItemSplitPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.SplitItem;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public ItemSplitPacketProcessor(AssetsLoader assets, ISender sender, ILogger logger)
        {
            _assets = assets;
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var originSlot = packet.ReadShort();
            var destinationSlot = packet.ReadShort();
            var amountToSplit = packet.ReadShort();

            var itemListMovimentation = UtilitiesFunctions.SwitchItemList(originSlot, destinationSlot);

            _logger.Debug($"Character {client.TamerId} splited {itemListMovimentation} from slot {originSlot} to {destinationSlot} x{amountToSplit}.");

            switch (itemListMovimentation)
            {
                case ItemListMovimentationEnum.InventoryToInventory:
                    {
                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(originSlot);

                        if (sourceItem.Amount < amountToSplit)
                        {
                            _logger.Error($"[DISCONNECTED] {client.Tamer.Name} try DUPPING {amountToSplit}x {sourceItem.ItemInfo.Name}, but he has {sourceItem.Amount}x on slot {originSlot} to {destinationSlot}!");
                            client.Disconnect();
                            return;
                        }

                        var temp = (ItemModel)sourceItem.Clone();

                        temp.SetAmount(amountToSplit);
                        
                        if (client.Tamer.Inventory.SplitItem(temp, destinationSlot))
                        {
                            sourceItem.ReduceAmount(amountToSplit);
                            client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));
                        }
                        else
                            client.Send(new SplitItemPacket(originSlot, destinationSlot, 0));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                    }
                    break;

                case ItemListMovimentationEnum.InventoryToWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Warehouse.FindItemBySlot(dstSlot);

                        if (destItem.Amount < amountToSplit)
                        {
                            _logger.Error($"[DISCONNECTED] {client.Tamer.Name} try DUPPING {amountToSplit}x {destItem.ItemInfo.Name}, but he has {destItem.Amount}x on slot {originSlot} to {destinationSlot}!");
                            client.Disconnect();
                            return;
                        }

                        if (destItem.ItemId > 0)
                        {
                            destItem.IncreaseAmount(amountToSplit);
                            sourceItem.ReduceAmount(amountToSplit);
                        }
                        else
                        {
                            var tempItem = (ItemModel)sourceItem.Clone();
                            tempItem.Amount = amountToSplit;
                            tempItem.SetItemInfo(sourceItem.ItemInfo);

                            client.Tamer.Warehouse.AddItemWithSlot(tempItem, dstSlot);
                            sourceItem.ReduceAmount(amountToSplit);
                        }

                        client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                    }
                    break;

                case ItemListMovimentationEnum.InventoryToAccountWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            destItem.IncreaseAmount(amountToSplit);
                            sourceItem.ReduceAmount(amountToSplit);
                        }
                        else
                        {
                            var tempItem = (ItemModel)sourceItem.Clone();
                            tempItem.Amount = amountToSplit;
                            tempItem.SetItemInfo(sourceItem.ItemInfo);

                            client.Tamer.AccountWarehouse.AddItemWithSlot(tempItem, dstSlot);
                            sourceItem.ReduceAmount(amountToSplit);
                        }

                        client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                    }
                    break;

                case ItemListMovimentationEnum.WarehouseToWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);

                        if (sourceItem.Amount < amountToSplit)
                        {
                            _logger.Error($"[DISCONNECTED] {client.Tamer.Name} try DUPPING {amountToSplit}x {sourceItem.ItemInfo.Name}, but he has {sourceItem.Amount}x on slot {originSlot} to {destinationSlot}!");
                            client.Disconnect();
                            return;
                        }

                        var temp = (ItemModel)sourceItem.Clone();

                        temp.SetAmount(amountToSplit);

                        if (client.Tamer.Warehouse.SplitItem(temp, dstSlot))
                        {
                            sourceItem.ReduceAmount(amountToSplit);
                            client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));
                        }
                        else
                            client.Send(new SplitItemPacket(originSlot, destinationSlot, 0));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                    }
                    break;

                case ItemListMovimentationEnum.WarehouseToInventory: // fix duplicação
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        // Validação: Certifique-se de que a quantidade solicitada para dividir não exceda a disponível no slot de origem
                        if (sourceItem.Amount < amountToSplit)
                        {
                            //fazer logica para adicionar a blacklist
                            //ou banir permanentemente

                            //client.Send(new DisconnectUserPacket($"A quantidade solicitada excede a disponível no armazém.").Serialize());
                            Console.WriteLine($"[ SISTEMA ] VOCE NAO TEM {amountToSplit} ITENS PARA DIVIDIR");
                            break; // Encerra a operação
                        }

                        if (destItem.ItemId > 0)
                        {
                            destItem.IncreaseAmount(amountToSplit);
                            sourceItem.ReduceAmount(amountToSplit);
                        }
                        else
                        {
                            var tempItem = (ItemModel)sourceItem.Clone();
                            tempItem.Amount = amountToSplit;
                            tempItem.SetItemInfo(sourceItem.ItemInfo);

                            client.Tamer.Inventory.AddItemWithSlot(tempItem, dstSlot);
                            sourceItem.ReduceAmount(amountToSplit);
                        }

                        client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                    }
                    break;


                case ItemListMovimentationEnum.WarehouseToAccountWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                        // Validação: Certifique-se de que a quantidade solicitada para dividir não exceda a disponível no slot de origem
                        if (sourceItem.Amount < amountToSplit)
                        {
                            //fazer logica para adicionar a blacklist
                            //ou banir permanentemente

                            //client.Send(new DisconnectUserPacket($"A quantidade solicitada excede a disponível no armazém.").Serialize());
                            Console.WriteLine($"[ SISTEMA ] VOCE NAO TEM {amountToSplit} ITENS PARA DIVIDIR");
                            break; // Encerra a operação
                        }

                        if (destItem.ItemId > 0)
                        {
                            destItem.IncreaseAmount(amountToSplit);
                            sourceItem.ReduceAmount(amountToSplit);
                        }
                        else
                        {
                            var tempItem = (ItemModel)sourceItem.Clone();
                            tempItem.Amount = amountToSplit;
                            tempItem.SetItemInfo(sourceItem.ItemInfo);

                            client.Tamer.AccountWarehouse.AddItemWithSlot(tempItem, dstSlot);
                            sourceItem.ReduceAmount(amountToSplit);
                        }

                        client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                    }
                    break;

                case ItemListMovimentationEnum.AccountWarehouseToAccountWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.AccountWarehouse.FindItemBySlot(srcSlot);

                        if (sourceItem.Amount < amountToSplit)
                        {
                            _logger.Error($"[DISCONNECTED] {client.Tamer.Name} try DUPPING {amountToSplit}x {sourceItem.ItemInfo.Name}, but he has {sourceItem.Amount}x on slot {originSlot} to {destinationSlot}!");
                            client.Disconnect();
                            return;
                        }

                        var temp = (ItemModel)sourceItem.Clone();

                        temp.SetAmount(amountToSplit);

                        if (client.Tamer.AccountWarehouse.SplitItem(temp, dstSlot))
                        {
                            sourceItem.ReduceAmount(amountToSplit);
                            client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));
                        }
                        else
                            client.Send(new SplitItemPacket(originSlot, destinationSlot, 0));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                    }
                    break;

                case ItemListMovimentationEnum.AccountWarehouseToInventory:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.AccountWarehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        // Validação: Certifique-se de que a quantidade solicitada para dividir não exceda a disponível no slot de origem
                        if (sourceItem.Amount < amountToSplit)
                        {
                            //fazer logica para adicionar a blacklist
                            //ou banir permanentemente

                            //client.Send(new DisconnectUserPacket($"A quantidade solicitada excede a disponível no armazém.").Serialize());
                            Console.WriteLine($"[ SISTEMA ] VOCE NAO TEM {amountToSplit} ITENS PARA DIVIDIR");
                            break; // Encerra a operação
                        }

                        if (destItem.ItemId > 0)
                        {
                            destItem.IncreaseAmount(amountToSplit);
                            sourceItem.ReduceAmount(amountToSplit);
                        }
                        else
                        {
                            var tempItem = (ItemModel)sourceItem.Clone();
                            tempItem.Amount = amountToSplit;
                            tempItem.SetItemInfo(sourceItem.ItemInfo);

                            client.Tamer.Inventory.AddItemWithSlot(tempItem, dstSlot);
                            sourceItem.ReduceAmount(amountToSplit);
                        }

                        client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                    }
                    break;

                case ItemListMovimentationEnum.AccountWarehouseToWarehouse:
                {
                        var srcSlot = originSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.AccountWarehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Warehouse.FindItemBySlot(dstSlot);

                        // Validação: Certifique-se de que a quantidade solicitada para dividir não exceda a disponível no slot de origem
                        if (sourceItem.Amount < amountToSplit)
                        {
                            //fazer logica para adicionar a blacklist
                            //ou banir permanentemente

                            //client.Send(new DisconnectUserPacket($"A quantidade solicitada excede a disponível no armazém.").Serialize());
                            Console.WriteLine($"[ SISTEMA ] VOCE NAO TEM {amountToSplit} ITENS PARA DIVIDIR");
                            break; // Encerra a operação
                        }

                        if (destItem.ItemId > 0)
                        {
                            destItem.IncreaseAmount(amountToSplit);
                            sourceItem.ReduceAmount(amountToSplit);
                        }
                        else
                        {
                            var tempItem = (ItemModel)sourceItem.Clone();
                            tempItem.Amount = amountToSplit;
                            tempItem.SetItemInfo(sourceItem.ItemInfo);

                            client.Tamer.Warehouse.AddItemWithSlot(tempItem, dstSlot);
                            sourceItem.ReduceAmount(amountToSplit);
                        }

                        client.Send(new SplitItemPacket(originSlot, destinationSlot, amountToSplit));

                        await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                    }
                    break;
            }

            client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
        }
    }
}