using DigitalWorldOnline.Commons.DTOs.Assets;

namespace DigitalWorldOnline.Commons.DTOs.Character
{
    public class CharacterEncyclopediaEvolutionsDTO
    {
        public long Id { get; set; }

        public long CharacterEncyclopediaId { get; set; }

        public int DigimonBaseType { get; set; }

        public byte SlotLevel { get; set; }

        public bool IsUnlocked { get; set; }

        public DateTime CreateDate { get; set; }

        public CharacterEncyclopediaDTO? Encyclopedia { get; set; }

        public DigimonBaseInfoAssetDTO? BaseInfo { get; set; }
    }
}