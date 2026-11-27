using HitAnalyzer2.Models;

namespace HitAnalyzer2.ViewModels
{
    public sealed class WeaponProfileItem
    {
        internal WeaponProfileItem(string fileName, WeaponProfileModel profile)
        {
            FileName = fileName;
            Profile = profile;
        }

        public string? Name => Profile.Name;
        public string Label => $"{Name} ({FileName})";
        internal string FileName { get; }
        internal WeaponProfileModel Profile { get; set; }
    }
}
