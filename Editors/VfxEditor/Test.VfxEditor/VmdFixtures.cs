namespace Test.VfxEditor
{
    // Variant mesh files laid out the way CA's are: CRLF, tab indents, " >" on an open tag, the tags on the mesh's closing line.
    static class VmdFixtures
    {
        public static string Lines(params string[] lines) => string.Join("\r\n", lines);

        // A unit's file: a body with two tags on its closing line, a weapon, and an empty weapon slot.
        public static readonly string Unit = Lines(
            "<VARIANT_MESH>",
            "\t<SLOT name=\"body\" >",
            "\t\t<VARIANT_MESH model=\"a\\body.wsmodel\" >",
            "\t\t\t<META_DATA>audio_entity_type:x</META_DATA>\t\t\t<META_DATA>audio_armour_type:leather</META_DATA></VARIANT_MESH>",
            "\t</SLOT>",
            "\t<SLOT name=\"weapon_1\" attach_point=\"be_prop_0\" >",
            "\t\t<VARIANT_MESH model=\"a\\sword.wsmodel\" />",
            "\t</SLOT>",
            "\t<SLOT name=\"weapon_2\" attach_point=\"be_prop_1\" />",
            "</VARIANT_MESH>");

        // dwf_gyrobomber's shape: a slot whose mesh has no model and holds slots of its own.
        public static readonly string Gyrobomber = Lines(
            "<VARIANT_MESH>",
            "\t<SLOT name=\"body\" >",
            "\t\t<VARIANT_MESH model=\"warmachines\\gyro.wsmodel\" >",
            "\t\t\t<META_DATA>audio_material:metal</META_DATA></VARIANT_MESH>",
            "\t</SLOT>",
            "\t<SLOT name=\"pilot\" >",
            "\t\t<VARIANT_MESH>",
            "\t\t\t<SLOT name=\"body\" >",
            "\t\t\t\t<VARIANT_MESH model=\"warmachines/pilot_body.wsmodel\" >",
            "\t\t\t\t\t<META_DATA>audio_armour_type:leather</META_DATA>\t\t\t\t\t<META_DATA>audio_entity_type:wh_gyrobomber</META_DATA></VARIANT_MESH>",
            "\t\t\t</SLOT>",
            "\t\t\t<SLOT name=\"head\" >",
            "\t\t\t\t<VARIANT_MESH model=\"warmachines/pilot_head.wsmodel\" />",
            "\t\t\t</SLOT>",
            "\t\t\t<SLOT name=\"helmet\" >",
            "\t\t\t\t<VARIANT_MESH model=\"warmachines/helmet_01.wsmodel\" />",
            "\t\t\t\t<VARIANT_MESH model=\"warmachines/helmet_02.wsmodel\" />",
            "\t\t\t\t<VARIANT_MESH_REFERENCE definition=\"variantmeshes/variantmeshdefinitions/helmets.variantmeshdefinition\" />",
            "\t\t\t</SLOT>",
            "\t\t</VARIANT_MESH>",
            "\t</SLOT>",
            "\t<SLOT name=\"weapon_1\" attach_point=\"be_prop_0\" />",
            "</VARIANT_MESH>");

        public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);
    }
}
