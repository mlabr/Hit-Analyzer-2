namespace HitAnalyzer2.ViewModels
{
    public sealed class WeaponProfileItem
    {
        internal WeaponProfileItem(string fileName, WeaponProfile profile)
        {
            FileName = fileName;
            Profile = profile;
        }

        public string Label => $"{Profile.Name} ({FileName})";
        internal string FileName { get; }
        internal WeaponProfile Profile { get; set; }
    }
}
