namespace DigitalWorldOnline.Commons.Models.Character
{
    public sealed partial class CharacterEncyclopediaModel
    {
        public DateTime TempUpdating { get; set; } = DateTime.Now;

        /// <summary>
        /// Creates a new seal.
        /// </summary>
        /// <param name="characterId">Character id.</param>
        /// <param name="digimonEvolutionId">Digimon evolution id.</param>
        /// <param name="isRewardAllowed">Is reward allowed.</param>
        /// <param name="isRewardReceived">Is reward received.</param>
        public static CharacterEncyclopediaModel Create(long characterId, long digimonEvolutionId, bool isRewardAllowed, bool isRewardReceived)
        {
            return new CharacterEncyclopediaModel()
            {
                CharacterId = characterId,
                DigimonEvolutionId = digimonEvolutionId,
                IsRewardAllowed = isRewardAllowed,
                IsRewardReceived = isRewardReceived
            };
        }
    }
}