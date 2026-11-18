using System;
using System.Collections.Generic;
using System.Linq;

using System.Text;
using System.Threading.Tasks;
using Shared.Core.ErrorHandling.Exceptions;
using SharpGLTF.Schema2;
using System.Windows;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.Core.PackFiles.Models;
using static Shared.GameFormats.Animation.AnimationFile;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;
using System.Security.Authentication.ExtendedProtection;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;
using Shared.GameFormats.RigidModel.Transforms;
using Editors.Shared.Core.Services;
using System.Windows.Media;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper
{
    public record AnimationBuilderSettings(
        ModelRoot modelRoot,
        string skeletonName,
        float keysPerSecond,
        IPackFileContainer packFileContainer,
        string packPath
    );

    public class AnimationBuilder  
    {        
        /// <param name="animationIndex">Which of the glTF's clips; each becomes its own .anim.</param>
        static public AnimationFile? Build(AnimationBuilderSettings settings, AnimationFile skeletonAnimFile, int animationIndex = 0)
        {
            if (settings.modelRoot.LogicalAnimations.Count() <= animationIndex)
                return null;

            AnimationFile newAnimFile = new AnimationFile();

            var animation = settings.modelRoot.LogicalAnimations[animationIndex];

            var animationTime = animation.Duration;
            var keyInterval = 1.0f / settings.keysPerSecond; // time between keys
            // A key at 0 and one at the end: truncating Duration / keyInterval dropped the last key, so every clip came back
            // one key short.
            var keyCount = (int)Math.Round(animationTime / keyInterval) + 1;
            
            newAnimFile.Header = new AnimationHeader()
            {
                Version = 7,    
                FrameRate = settings.keysPerSecond,
                SkeletonName = settings.skeletonName,

                // The time of the last key, as the game reads it.
                AnimationTotalPlayTimeInSec = keyInterval * (float)(keyCount - 1)
            };

            newAnimFile.Bones = skeletonAnimFile.Bones;
            newAnimFile.AnimationParts = new AnimationPart[1].ToList(); // ALLOCATE ONE PART
            newAnimFile.AnimationParts[0] = new AnimationPart();
            var part = newAnimFile.AnimationParts[0];

            part.DynamicFrames = new List<Frame>();

            DoQuantization(skeletonAnimFile, part);

            for (int i = 0; i < keyCount; i++)
            {
                var frame = new Frame();
                FillFrame(settings, animation, skeletonAnimFile, i * keyInterval, frame);
                part.DynamicFrames.Add(frame);
            }

            return newAnimFile;
        }

        private static void DoQuantization(AnimationFile skeletonAnimFile, AnimationPart part)
        {
            /*
            -- TODO: add ANIM v6 quantization ---
           
            - which track are both constant and = bind pose?
            - iterate through tracks, 
                 - if track is constant and = bind pose, 
                    - insert "-1" into the quantization list
                        - don't store the track in the frame
                        
            -- TODO: add ANIM v7 quantization ---
            - which track are both constant and which are = bind pose?
            - iterate through tracks, 
                 - if track is constant and = bind pose, 
                    - insert "-1" into the quantization list
                        - don't store the track in the frame
                 - if constant but != bind pose
                 - adds its value to const track(aka static frame)
                 - in the quantization list, insert the index of the const track + 10000        
            
            */


            // set the quantization settings to UNquantized
            for (int i = 0; i < skeletonAnimFile.Bones.Length; i++)
            {
                part.TranslationMappings.Add(new AnimationBoneMapping(i));
                part.RotationMappings.Add(new AnimationBoneMapping(i));
            }
        }

        private static void FillFrame(AnimationBuilderSettings settings, Animation animation, AnimationFile skeletonAnimFile, float currentKeyTime, Frame frame)
        {
            RmvVector3[] translations = new RmvVector3[skeletonAnimFile.Bones.Length];
            RmvVector4[] quaternions = new RmvVector4[skeletonAnimFile.Bones.Length];

            foreach (var bone in skeletonAnimFile.Bones)
            {
                var translation = GltfAnimationTrackSampler.SampleTranslation(settings.modelRoot, animation, bone.Name, currentKeyTime);
                var quaternion = GltfAnimationTrackSampler.SampleQuaternion(settings.modelRoot, animation, bone.Name, currentKeyTime);

                // using the arrays with bone.id as index, makes sure sampled data is stored at the index
                translations[bone.Id] = new RmvVector3(translation);
                quaternions[bone.Id] = new RmvVector4(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
            }

            frame.Transforms = translations.ToList();
            frame.Quaternion = quaternions.ToList();
        }
    }
}
