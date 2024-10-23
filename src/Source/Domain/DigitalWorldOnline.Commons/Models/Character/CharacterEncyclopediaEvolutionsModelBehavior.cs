namespace DigitalWorldOnline.Commons.Models.Character
{
    public sealed partial class CharacterEncyclopediaEvolutionsModel
    {
        public DateTime TempUpdating { get; set; } = DateTime.Now;

        /// <summary>
        /// Creates a new encyclopedia evolution.
        /// </summary>
        /// <param name="characterEncyclopediaId">Encyclopedia id</param>
        /// <param name="digimonBaseType">Digimon base type</param>
        /// <param name="isUnlocked">Is unlocked</param>
        public static CharacterEncyclopediaEvolutionsModel Create(long characterEncyclopediaId, int digimonBaseType, bool isUnlocked)
        {
            return new CharacterEncyclopediaEvolutionsModel()
            {
                CharacterEncyclopediaId = characterEncyclopediaId,
                DigimonBaseType = digimonBaseType,
                IsUnlocked = isUnlocked,
            };
        }
    }
}