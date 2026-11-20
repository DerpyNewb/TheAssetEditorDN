using GameWorld.Core.Services.SceneSaving.Geometry;
using GameWorld.Core.Services.SceneSaving.Lod;
using GameWorld.Core.Services.SceneSaving.Material;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.LodHeader;
using Shared.GameFormats.RigidModel.Types;

namespace GameWorld.Core.Services.SceneSaving
{
    public class GeometrySaveSettings
    {
        private readonly ApplicationSettingsService _applicationSettingsService;

        public bool IsUserInitialized { get; set; } = false;    // Initialized after the user has opened a settings window once
        public string OutputName { get; set; } = "";// Init on load
        public GeometryStrategy GeometryOutputType { get; set; } = GeometryStrategy.Rmv7;
        public MaterialStrategy MaterialOutputType { get; set; } = MaterialStrategy.WsModel_Warhammer3;
        public LodStrategy LodGenerationMethod { get; set; } = LodStrategy.MeshOptimizer;
        public List<LodGenerationSettings> LodSettingsPerLod { get; set; } = [];
        public bool OnlySaveVisible { get; set; } = true;
        public int NumberOfLodsToGenerate { get; set; } = 4;
        public List<RmvAttachmentPoint> AttachmentPoints { get; set; } = [];
        public bool DisplayDialogOnNextSave { get; set; } = true;

        public GeometrySaveSettings(ApplicationSettingsService applicationSettingsService)
        {
            _applicationSettingsService = applicationSettingsService;

            GameTypeEnum[] rmv7Games = [
                GameTypeEnum.Warhammer3, GameTypeEnum.ThreeKingdoms,    // Actually rmv8, but not supported by tool for now
                GameTypeEnum.Pharaoh, GameTypeEnum.Troy, GameTypeEnum.Warhammer2, GameTypeEnum.Warhammer];

            if (rmv7Games.Contains(_applicationSettingsService.CurrentSettings.CurrentGame) == false)
                GeometryOutputType = GeometryStrategy.Rmv6;

            MaterialOutputType = _applicationSettingsService.CurrentSettings.CurrentGame switch
            {
                GameTypeEnum.Warhammer3 => MaterialStrategy.WsModel_Warhammer3,
                GameTypeEnum.Warhammer2 => MaterialStrategy.WsModel_Warhammer2,
                GameTypeEnum.Pharaoh => MaterialStrategy.WsModel_Pharaoh,
                _ => MaterialStrategy.None,
            };

            ApplyLastChoices();
        }

        // Upstream #322: each new Kitbash editor started from the defaults again.
        void ApplyLastChoices()
        {
            var settings = _applicationSettingsService.CurrentSettings;
            var last = settings.KitbashSaveChoices;
            if (last == null || last.Game != settings.CurrentGame)
                return;

            if (Enum.TryParse<GeometryStrategy>(last.GeometryStrategy, out var geometry))
                GeometryOutputType = geometry;
            if (Enum.TryParse<MaterialStrategy>(last.MaterialStrategy, out var material))
                MaterialOutputType = material;
            if (Enum.TryParse<LodStrategy>(last.LodStrategy, out var lod))
                LodGenerationMethod = lod;
            OnlySaveVisible = last.OnlySaveVisible;
            DisplayDialogOnNextSave = last.DisplayDialogOnNextSave;
        }

        /// <summary>Keeps the save dialog's choices in the app settings, so the next editor's save offers them. Output path and
        /// LOD settings stay per model.</summary>
        public void RememberChoices()
        {
            var settings = _applicationSettingsService.CurrentSettings;
            settings.KitbashSaveChoices = new KitbashSaveChoices(settings.CurrentGame, GeometryOutputType.ToString(), MaterialOutputType.ToString(),
                LodGenerationMethod.ToString(), OnlySaveVisible, DisplayDialogOnNextSave);
            _applicationSettingsService.Save();
        }

        public void RefreshLodSettings()
        {
            LodSettingsPerLod.Clear();
            for (var i = 0; i < NumberOfLodsToGenerate; i++)
            {
                var settings = GenerateLodSettingsForIndex(i, NumberOfLodsToGenerate, null);
                LodSettingsPerLod.Add(settings);
            }
        }

        public void InitializeLodSettings(RmvLodHeader[] lodHeaders)
        {
            LodSettingsPerLod.Clear();
            NumberOfLodsToGenerate = lodHeaders.Length;

            for (var i = 0; i < NumberOfLodsToGenerate; i++)
            {
                var settings = GenerateLodSettingsForIndex(i, NumberOfLodsToGenerate, lodHeaders);
                LodSettingsPerLod.Add(settings);
            }
        }

        // Medians over vanilla WH3 unit models: camera distances 10, 20, 30 with the last LOD at 10000, quality 2
        // for LOD0 and 0 after, triangles kept 1, 0.65, 0.31, 0.105, 0.027. Rome 2/Attila unit models (RMV2 v6)
        // use distances 100, 200, 400, 500 and reduce about as much.
        public const float LastLodCameraDistance = 10000;
        static readonly float[] s_defaultLodReductionFactors = [1, 0.65f, 0.3f, 0.1f, 0.03f];
        static readonly float[] s_rmv6LodCameraDistances = [100, 200, 400, 500];

        float GetDefaultCameraDistance(int lodIndex, int lodCount)
        {
            if (GeometryOutputType == GeometryStrategy.Rmv6)
                return lodIndex < s_rmv6LodCameraDistances.Length
                    ? s_rmv6LodCameraDistances[lodIndex]
                    : s_rmv6LodCameraDistances[^1] + 100 * (lodIndex - s_rmv6LodCameraDistances.Length + 1);

            var isLastLod = lodIndex == lodCount - 1;
            return isLastLod ? LastLodCameraDistance : 10 * (lodIndex + 1);
        }

        LodGenerationSettings GenerateLodSettingsForIndex(int lodIndex, int lodCount, RmvLodHeader[]? lodHeaders)
        {
            var cameraDistance = GetDefaultCameraDistance(lodIndex, lodCount);
            var qualityLvl = (byte)(lodIndex == 0 ? 2 : 0);

            if (lodHeaders != null)
            {
                var numLodsInGeometry = lodHeaders.Length;
                if (lodIndex < numLodsInGeometry)
                {
                    var lodHeader = lodHeaders[lodIndex];
                    cameraDistance = lodHeader.LodCameraDistance;
                    qualityLvl = lodHeader.QualityLvl;
                }
            }

            var setting = new LodGenerationSettings()
            {
                CameraDistance = cameraDistance,
                QualityLvl = qualityLvl,
                LodRectionFactor = s_defaultLodReductionFactors[Math.Min(lodIndex, s_defaultLodReductionFactors.Length - 1)],
                OptimizeAlpha = lodIndex >= 2,
                OptimizeVertex = lodIndex >= 2,
            };
            return setting;
        }
    }

    public class LodGenerationSettings
    {
        public float LodRectionFactor { get; set; }
        public bool OptimizeAlpha { get; set; }
        public bool OptimizeVertex { get; set; }
        public byte QualityLvl { get; set; }
        public float CameraDistance { get; set; }
    }
}
