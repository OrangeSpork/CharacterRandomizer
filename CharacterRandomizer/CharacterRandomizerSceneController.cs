using ExtensibleSaveFormat;
using KKAPI.Chara;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CharacterRandomizer
{
    public class CharacterRandomizerSceneController : SceneCustomFunctionController
    {
        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            if (operation == SceneOperationKind.Load)
            {
                CharacterRandomizerPlugin.CurrentMaleCharacters.Clear();
                CharacterRandomizerPlugin.CurrentFemaleCharacters.Clear();

                CharacterRandomizerPlugin.NextReplacementTime = 0f;
                // Clear the loaded flags
                CharacterApi.ControllerRegistration controllerRegistration = CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID);
                foreach (CharacterRandomizerCharaController charaController in controllerRegistration.Instances)
                {
                    if (charaController.Running)
                        charaController.ScheduleNextReplacement(true);

                    charaController.UpdateCurrentCharacterRegistry(charaController.LastReplacementFile);
                }

                CharacterRandomizer.CharacterRandomizerPlugin.Instance.ScanForFolderFlags();
            }
            else if (operation == SceneOperationKind.Clear)
            {
                CharacterRandomizerPlugin.FolderRequestFlags.Clear();
                CharacterRandomizerPlugin.CurrentMaleCharacters.Clear();
                CharacterRandomizerPlugin.CurrentFemaleCharacters.Clear();
                CharacterRandomizerPlugin.NextReplacementTime = 0f;
            }
        }

        protected override void OnSceneSave()
        {

        }
    }
}
