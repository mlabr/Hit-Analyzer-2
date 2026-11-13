namespace HitAnalyzer2.Services.Shooting
{
    public sealed class WeaponOption
    {
        internal WeaponOption(string fileName, WeaponProfile profile)
        {
            FileName = fileName;
            Profile = profile;
        }

        public string Label => $"{Profile.Name} ({FileName})";
        internal string FileName { get; }
        internal WeaponProfile Profile { get; set; }
    }
}
