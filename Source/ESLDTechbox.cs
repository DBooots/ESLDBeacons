using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using UnityEngine;

namespace ESLDCore
{
    public class ESLDTechbox : PartModule
    {
        [KSPField]
        public string animationName = "";

        Animation anim;

        [KSPField(guiName = "Status", isPersistant = true, guiActive = true)]
        public string techBoxStatus;

        [KSPField(isPersistant = true, guiActive = false)]
        public bool activated = false;

        [KSPField(guiActive = false)]
        public string techBoxModel;

        [KSPField]
        public bool alwaysActive = false;

        protected void ForceUpdateTechboxes()
        {
            foreach (ESLDBeacon beacon in vessel.FindPartModulesImplementing<ESLDBeacon>())
            {
                beacon.CheckOwnTechBoxes();
            }
        }

        [KSPEvent(name = "TechBoxOn", active = true, guiActive = true, guiName = "Activate")]
        public void TechBoxOn()
        {
            if (!activated)
            {
                part.force_activate();
                activated = true;
                ForceUpdateTechboxes();
                SetEventsActions(true);
                PlayAnimation(1f);
            }
            else
                Debug.LogWarning("ESLDCore:ESLDTechbox: Can only activate when deactivated!");
        }
        
        [KSPEvent(name = "TechBoxOff", active = false, guiActive = true, guiName = "Deactivate")]
        public void TechBoxOff()
        {
            if (activated)
            {
                activated = false;
                ForceUpdateTechboxes();
                SetEventsActions(false);
                PlayAnimation(-1f);
            }
            else
                Debug.LogWarning("ESLDCore:ESLDTechbox: Can only deactivate when activated!");
        }

        [KSPAction("Toggle TechBox")]
        public void ToggleTBAction(KSPActionParam param)
        {
            if (activated)
                TechBoxOff();
            else
                TechBoxOn();
        }
        [KSPAction("Activate TechBox")]
        public void ActivateTBAction(KSPActionParam param)
            => TechBoxOn();
        [KSPAction("Deactivate TechBox")]
        public void DeactivateTBAction(KSPActionParam param)
            => TechBoxOff();

        public override void OnStart(StartState state)
        {
            if (anim != null)
            {
#if DEBUG
                Debug.Log("ESLDCore:ESLDTechbox: Animation found: " + animationName);
#endif
                anim[animationName].wrapMode = WrapMode.Once;
                if (activated)
                {
                    anim[animationName].normalizedTime = 1;
                    anim.Play(animationName);
                }
                else
                {
                    anim[animationName].normalizedTime = 0;
                    anim[animationName].normalizedSpeed = -10;
                    anim.Play(animationName);
                }
            }
            if (alwaysActive)
            {
                part.force_activate();
                activated = true;
                ForceUpdateTechboxes();
                SetEventsActions(true);
                Events["TechBoxOff"].active = false;
                Actions["DeactivateTBAction"].active = false;
                Actions["ToggleTBAction"].active = false;
                PlayAnimation(1f);
            }
        }

        private void SetEventsActions(bool activated)
        {
            techBoxStatus = techBoxModel + (activated ? " Active." : " Inactive.");
            Events["TechBoxOn"].active = !activated;
            Events["TechBoxOff"].active = activated;
            Actions["ActivateTBAction"].active = !activated;
            Actions["DeactivateTBAction"].active = activated;
        }

        private void PlayAnimation(float speed)
        {
            if (anim == null) return;
            anim[animationName].normalizedSpeed = speed;
            anim.Play(animationName);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (animationName != "")
            {
                anim = part.FindModelAnimators(animationName).FirstOrDefault();
                if (anim == null)
                    Debug.LogWarning("ESLDCore:ESLDTechbox: Animation not found! " + animationName);
            }
        }

        public override string GetInfo()
        {
            StringBuilder info = new StringBuilder();

            info.AppendLine("Techbox Type: " + techBoxModel);
            if (alwaysActive)
                info.AppendLine("<color=#99ff00ff>Always activated</color>");
            return info.ToString().TrimEnd(Environment.NewLine.ToCharArray());
        }
    }
}
