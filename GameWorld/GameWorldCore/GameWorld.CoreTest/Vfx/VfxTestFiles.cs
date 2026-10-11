using System.Text;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;

namespace GameWorld.Core.Test.Vfx
{
    // Small vfx files written the way CA's are (UTF-16 with a byte order mark, several root
    // elements), in an in-memory pack under vfx/.
    static class VfxTestFiles
    {
        public static byte[] Xml(string emitters = "", string chains = "", string references = "", string stacks = "") =>
        [
            .. Encoding.Unicode.GetPreamble(),
            .. Encoding.Unicode.GetBytes(
                $"""<shader_library file="fx/particle_vfx_library.hlsl"/><vfx revision="2" construction_type="1" enable_in_ted="false"><emitters>{emitters}</emitters><behaviour_chains>{chains}</behaviour_chains><effect_render_stacks>{stacks}</effect_render_stacks></vfx><vfx_references>{references}</vfx_references><!-- EDITOR DATA--><editor_data></editor_data>"""),
        ];

        public static string Emitter(string id, string properties = "") =>
            $"""<emitter type="continuous_mesh" id="{id}"><property name="InfiniteLife" quality="0" value="true"/>{properties}</emitter>""";

        public static string Reference(string target, string posX = "0.00", string posY = "0.00", string rotY = "0.00", string overrides = "") =>
            $"""<vfx inst_name="xref" vfx_ref="{target}" notes="" scale_min="1.00" scale_max="1.00" pos_x="{posX}" pos_y="{posY}" pos_z="0.00" rot_x="0.00" rot_y="{rotY}" rot_z="0.00" start_time_min="0.00" start_time_max="0.00" infinite_life="false" simulation_speed_multiplier="1.00">{overrides}</vfx>""";

        public static string Override(string name, bool enable = true, string tint = "1.00") =>
            $"""<emitter name="{name}" enable="{(enable ? "true" : "false")}" tint_min_r="{tint}" tint_min_g="{tint}" tint_min_b="{tint}" tint_min_a="1.00" tint_max_r="{tint}" tint_max_g="{tint}" tint_max_b="{tint}" tint_max_a="1.00" particle_lifetime_bias_min="0.00" particle_lifetime_bias_max="0.00" emitter_lifetime_bias_min="0.00" emitter_lifetime_bias_max="0.00" spawn_rate_scale_factor_min="1.00" spawn_rate_scale_factor_max="1.00" sound_record=""/>""";

        public static IPackFileService Pack(params (string Name, byte[] Bytes)[] files) =>
            Pack(files.Select(f => PackFile.CreateFromBytes(f.Name, f.Bytes)));

        public static IPackFileService Pack(IEnumerable<PackFile> files)
        {
            var packFileService = new PackFileService(null) { EnforceGameFilesMustBeLoaded = false };
            var container = packFileService.CreateNewPackFileContainer("output", PackFileVersion.PFH5, PackFileCAType.MOD, true);
            packFileService.AddFilesToPack(container, files.Select(f => new NewPackFileEntry("vfx", f)).ToList());
            return packFileService;
        }
    }
}
