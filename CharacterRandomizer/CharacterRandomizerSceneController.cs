using KKAPI.Chara;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
using System;
using System.Collections.Generic;
using System.Text;

namespace CharacterRandomizer
{
    public class CharacterRandomizerSceneController : SceneCustomFunctionController
    {
        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            if (operation == SceneOperationKind.Load)
            {
                CharacterRandomizerPlugin.CurrentCharacters.Clear();
                CharacterRandomizerPlugin.NextReplacementTime = float.MaxValue;
                // Clear the loaded flags
                CharacterApi.ControllerRegistration controllerRegistration = CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID);
                foreach (CharacterRandomizerCharaController charaController in controllerRegistration.Instances)
                {
                    charaController.ScheduleNextReplacement(true);
                }
            }
        }

        protected override void OnSceneSave()
        {

        }
    }
}
