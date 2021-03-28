using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.UI.Screens;

namespace ESLDCore
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class HailerButton : MonoBehaviour
    {
        public static HailerButton Instance;
        public ApplicationLauncherButton button;
        private Vessel vessel;
        private ESLDHailer hailer;
        public bool canHail = false;
        private Texture2D ESLDButtonOn;// = new Texture2D(38, 38, TextureFormat.ARGB32, false);
        private FlightCamera mainCam = null;
        private bool isDazzling = false;
        private float currentFOV = 60;
        private float userFOV = 60;
        private float currentDistance = 1;
        private float userDistance = 1;

        public void Update()
        {
            if (isDazzling)
            {
                currentFOV = Mathf.Lerp(currentFOV, userFOV, 0.04f);
                currentDistance = Mathf.Lerp(currentDistance, userDistance, 0.04f);
                mainCam.SetFoV(currentFOV);
                mainCam.SetDistance(currentDistance);
                if (userFOV + 0.25 >= currentFOV)
                {
                    mainCam.SetFoV(userFOV);
                    mainCam.SetDistance(userDistance);
                    isDazzling = false;
                    Debug.Log("ESLDCore: Done messing with camera!");
                }
            }
        }

        public void Awake()
        {
            if (ESLDButtonOn == null)
                ESLDButtonOn = GameDatabase.Instance.GetTexture("ESLDBeacons/Textures/launcher", false);
            if (Instance != null)
                Destroy(Instance);
            Instance = this;
            //GameEvents.onGUIApplicationLauncherReady.Add(onGUIApplicationLauncherReady);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneChangeRequest);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(KillButton);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequestedForAppLauncher);
            mainCam = FlightCamera.fetch;
        }

        public void OnDestroy()
        {
            //GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIApplicationLauncherReady);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneChangeRequest);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(KillButton);
            KillButton();
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequestedForAppLauncher);
        }

        private void OnTrue()
        {
            OnVesselChange(FlightGlobals.ActiveVessel);
            HailerGUI.ActivateGUI(FlightGlobals.ActiveVessel);
        }

        private void OnFalse()
            => HailerGUI.CloseAllGUIs();


        private void OnGUIApplicationLauncherReady()
        {
            if (this.button != null)
            {
                KillButton();
            }
            if (canHail)
            {
                this.button = ApplicationLauncher.Instance.AddModApplication(
                    this.OnTrue,
                    this.OnFalse,
                    null,
                    null,
                    null,
                    null,
                    ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                    ESLDButtonOn);
            }
        }

        public void OnSceneChangeRequest(GameScenes _scene)
            => KillButton();

        public void OnVesselChange(Vessel vessel)
        {
            HailerGUI.CloseGUI(this.vessel);
            this.vessel = vessel;

            hailer = vessel?.FindPartModulesImplementing<ESLDHailer>().FirstOrDefault();

            canHail = hailer != null;

            if (canHail && button == null)
                OnGUIApplicationLauncherReady();
            else if (!canHail && button != null)
                KillButton();
        }

        private void KillButton()
        {
            HailerGUI.CloseAllGUIs();
            if (button != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(button);
                button = null;
            }
        }

        void OnGameSceneLoadRequestedForAppLauncher(GameScenes SceneToLoad)
            => KillButton();

        // Warp Effect
        public void Dazzle()
        {
            userFOV = mainCam.FieldOfView;
            userDistance = mainCam.Distance;
            currentFOV = 180;
            currentDistance = 0.1f;
            isDazzling = true;
            Debug.Log("ESLDCore: Messing with camera!");
        }
    }
}
