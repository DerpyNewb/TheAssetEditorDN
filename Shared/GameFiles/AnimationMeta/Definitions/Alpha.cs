// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Shared.GameFormats.AnimationMeta.Parsing;

namespace Shared.GameFormats.AnimationMeta.Definitions
{
    [MetaData("ALPHA", 10)]
    public class Alpha_v10 : DecodedMetaEntryBase
    {
        [MetaDataTag(5, "This might be a %, 0 = invisible and 1 = visible.")]
        public float DesiredAlpha { get; set; }
    }

    // Same layout as ALPHA_10. Vanilla WH3 uses it on Nagash's special-ability spellcast, value 1.
    [MetaData("VFX_ALPHA", 10)]
    public class VfxAlpha_v10 : DecodedMetaEntryBase
    {
        [MetaDataTag(5, "0 = invisible, 1 = fully visible.")]
        public float DesiredAlpha { get; set; } = 1;
    }
}
