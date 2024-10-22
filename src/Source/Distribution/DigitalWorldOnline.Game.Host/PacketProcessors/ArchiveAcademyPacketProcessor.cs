﻿using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using MediatR;
using Serilog;
using System.Net.Sockets;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ArchiveAcademyPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.DigimonArchiveAcademy;

        private readonly ILogger _logger;

        public ArchiveAcademyPacketProcessor(
            ILogger logger)
        {
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {

            var packet = new GamePacketReader(packetData);
            client.Send(new ArchiveAcademyIniciarPacket());
        }
    }
}

