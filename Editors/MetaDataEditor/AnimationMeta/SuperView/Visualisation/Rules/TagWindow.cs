namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    public static class TagWindow
    {
        /// <summary>A timed tag's strength from 0 to 1: full from start to end, fading in over blendIn before start and out over
        /// blendOut after end. A tag at 0 to 0 with no blends holds for the whole clip. Vanilla's own timing implies this reading,
        /// not a test in game: 1,744 TRANSFORM tags have blends longer than their window, 225 TRANSFORM and 486 SPLICE tags
        /// start and end at one moment with blends, and 2,255 TRANSFORM and 5,011 SPLICE tags run 0 to 0 with none. Many of the
        /// 640 SPLICE tags at 0 to 0 with blends are a stance pose in an attack, a defend or a death that fades out early in
        /// the clip, which is what this reading draws.</summary>
        public static float Strength(float start, float end, float blendIn, float blendOut, float time)
        {
            if (start == 0 && end == 0 && blendIn == 0 && blendOut == 0)
                return 1;
            if (time < start)
                return blendIn > 0 && time > start - blendIn ? (time - (start - blendIn)) / blendIn : 0;
            if (time <= end)
                return 1;
            return blendOut > 0 && time < end + blendOut ? 1 - (time - end) / blendOut : 0;
        }
    }
}
