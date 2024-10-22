﻿namespace DigitalWorldOnline.Commons.ViewModel.SpawnPoint
{
    public class SpawnPointUpdateViewModel
    {
        /// <summary>
        /// Unique sequential identifier.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Region index.
        /// </summary>
        public byte Index { get; set; }

        /// <summary>
        /// Axys X position. 
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Axys Y position.
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Region name.
        /// </summary>
        public string Name { get; set; }

        public bool Invalid => string.IsNullOrEmpty(Name) || Index == 0;

        public SpawnPointUpdateViewModel()
        {
            Index = 1;
        }
    }
}