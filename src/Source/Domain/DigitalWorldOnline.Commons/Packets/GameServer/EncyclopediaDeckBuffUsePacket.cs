using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Commons.Writers;
using System.Collections.Generic;
using static System.Reflection.Metadata.BlobBuilder;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class EncyclopediaDeckBuffUsePacket : PacketWriter
    {
        private const int PacketNumber = 3236;

        public EncyclopediaDeckBuffUsePacket(CharacterModel character)
        {
            Type(PacketNumber);

            WriteInt(character.Partner.HP);
            WriteShort((short)character.Partner.AS);
        }
    }
}