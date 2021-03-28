using System;
using System.Collections;

namespace ESLDCore
{
    public abstract class ModuleDeployableGenericPart : PartModule
    {
        [KSPField]
        public int deployAnimationController = -1;
        [KSPField(isPersistant = true)]
        public int _lockCount = 0;

        public enum DeployState
        {
            Stowed = 0,
            Stowing = 1,
            Deploying = 2,
            Deployed = 3
        }

        public DeployState State
        {
            get
            {
                if (deployAnimator == null)
                    return DeployState.Deployed;
                if (!fsm.Started)
                    return DeployState.Stowed;

                string value = fsm.currentStateName;
                if (value.StartsWith("stowed", StringComparison.InvariantCultureIgnoreCase))
                    return DeployState.Stowed;
                else if (value.StartsWith("deployed", StringComparison.InvariantCultureIgnoreCase))
                    return DeployState.Deployed;
                else if (value.StartsWith("stowing", StringComparison.InvariantCultureIgnoreCase))
                    return DeployState.Stowing;
                else if (value.StartsWith("deploying", StringComparison.InvariantCultureIgnoreCase))
                    return DeployState.Deploying;
                else
                    return DeployState.Deployed;
            }
        }

        public ModuleAnimateGeneric deployAnimator;

        protected string deployStateOnLoad = "stowed";

        public KerbalFSM fsm;
        public KFSMState st_stowed;
        public KFSMState st_stowed_locked;
        public KFSMState st_deploying;
        public KFSMState st_deployed;
        public KFSMState st_deployed_locked;
        public KFSMState st_stowing;
        public KFSMEvent on_stowed;
        public KFSMEvent on_lock_stowed;
        public KFSMEvent on_unlock_stowed;
        public KFSMEvent on_lock_deployed;
        public KFSMEvent on_unlock_deployed;
        public KFSMEvent on_stow;
        public KFSMEvent on_deployed;
        public KFSMEvent on_deploy;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (deployAnimationController > -1)
            {
                deployAnimator = part.Modules.GetModule(deployAnimationController) as ModuleAnimateGeneric;
            }
            if (deployAnimator == null)
                deployStateOnLoad = "deployed";

            StartCoroutine(LateFMSStart(state));
        }

        private IEnumerator LateFMSStart(StartState state)
        {
            SetupFSM();
            if ((state & StartState.Editor) == StartState.None)
                fsm.StartFSM(deployStateOnLoad);

            if (deployAnimator != null)
                deployAnimator.OnMoving.Add(OnAnimationMove);

            yield break;
        }

        public void SetupFSM()
        {
            fsm = new KerbalFSM();

            st_stowed = new KFSMState("stowed");
            fsm.AddState(st_stowed);
            st_stowed_locked = new KFSMState("stowed_locked");
            fsm.AddState(st_stowed_locked);
            st_deploying = new KFSMState("deploying");
            fsm.AddState(st_deploying);
            st_deployed = new KFSMState("deployed");
            fsm.AddState(st_deployed);
            st_deployed_locked = new KFSMState("deployed_locked");
            fsm.AddState(st_deployed_locked);
            st_stowing = new KFSMState("stowing");
            fsm.AddState(st_stowing);

            on_deploy = new KFSMEvent("on_deploy") { GoToStateOnEvent = st_deploying };
            fsm.AddEvent(on_deploy, st_stowed, st_stowing);
            on_stow = new KFSMEvent("on_stow") { GoToStateOnEvent = st_stowing };
            fsm.AddEvent(on_stow, st_deployed, st_deploying);
            on_deployed = new KFSMEvent("on_deployed") { GoToStateOnEvent = st_deployed, updateMode = KFSMUpdateMode.UPDATE, OnCheckCondition = (st) => (deployAnimator?.animTime ?? 1) == 1 };
            fsm.AddEvent(on_deployed, st_deploying);
            on_stowed = new KFSMEvent("on_stowed") { GoToStateOnEvent = st_stowed, updateMode = KFSMUpdateMode.UPDATE, OnCheckCondition = (st) => (deployAnimator?.animTime ?? 0) == 0 };
            fsm.AddEvent(on_stowed, st_stowing);
            on_lock_deployed = new KFSMEvent("on_lock_deployed") { GoToStateOnEvent = st_deployed_locked, OnEvent = DisableAnimation };
            fsm.AddEvent(on_lock_deployed, st_deployed);
            on_unlock_deployed = new KFSMEvent("on_unlock_deployed") { GoToStateOnEvent = st_deployed, OnEvent = EnableAnimation };
            fsm.AddEvent(on_unlock_deployed, st_deployed_locked);
            on_lock_stowed = new KFSMEvent("on_lock_stowed") { GoToStateOnEvent = st_stowed_locked, OnEvent = DisableAnimation };
            fsm.AddEvent(on_lock_stowed, st_stowed);
            on_unlock_stowed = new KFSMEvent("on_unlock_stowed") { GoToStateOnEvent = st_stowed, OnEvent = EnableAnimation };
            fsm.AddEvent(on_unlock_stowed, st_stowed_locked);

            //fsm.OnStateChange = (fromSt, toSt, e) => UnityEngine.Debug.LogFormat("Moved from {0} to {1} by {2}", fromSt.name, toSt.name, e.name);

            CustomizeFSM();
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            string stateStr = "stowed";
            if (fsm != null && fsm.Started)
                stateStr = fsm.currentStateName;

            node.AddValue("deployState", stateStr);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (node.HasValue("deployState"))
                deployStateOnLoad = node.GetValue("deployState");
        }

        virtual public void FixedUpdate()
        {
            if (fsm != null && fsm.Started)
                fsm.FixedUpdateFSM();
        }

        virtual public void Update()
        {
            if (fsm != null && fsm.Started)
                fsm.UpdateFSM();
        }

        virtual public void LateUpdate()
        {
            if (fsm != null && fsm.Started)
                fsm.LateUpdateFSM();
        }

        virtual protected void CustomizeFSM() { }

        public bool Lock()
        {
            if (deployAnimator == null)
                return true;

            if (!fsm.currentStateName.StartsWith("deployed", StringComparison.InvariantCultureIgnoreCase) && !fsm.currentStateName.StartsWith("stowed", StringComparison.InvariantCultureIgnoreCase))
                return false;

            if (_lockCount == 0)
            {
                if (fsm.CurrentState == st_stowed)
                    fsm.RunEvent(on_lock_stowed);
                else if (fsm.CurrentState == st_deployed)
                    fsm.RunEvent(on_lock_deployed);
            }

            _lockCount += 1;
            return true;
        }

        public bool Unlock()
        {
            if (deployAnimator == null)
                return true;

            if (!fsm.currentStateName.StartsWith("deployed", StringComparison.InvariantCultureIgnoreCase) && !fsm.currentStateName.StartsWith("stowed", StringComparison.InvariantCultureIgnoreCase))
                return false;
            if (_lockCount == 0)
                return true;

            _lockCount -= 1;

            if (_lockCount == 0)
            {
                if (fsm.CurrentState == st_stowed_locked)
                    fsm.RunEvent(on_unlock_stowed);
                else if (fsm.CurrentState == st_deployed_locked)
                    fsm.RunEvent(on_unlock_deployed);
            }

            return true;
        }

        private void OnAnimationMove(float from, float to)
        {
            if (to >= 1)
                fsm.RunEvent(on_deploy);
            else if (to <= 0)
                fsm.RunEvent(on_stow);
        }

        protected void EnableAnimation()
        {
            if (deployAnimator == null)
                return;
            deployAnimator.Events["Toggle"].active = true;
            deployAnimator.Actions["ToggleAction"].active = true;
            deployAnimator.SetUIWrite(true);
        }

        protected void DisableAnimation()
        {
            if (deployAnimator == null)
                return;
            deployAnimator.Events["Toggle"].active = false;
            deployAnimator.Actions["ToggleAction"].active = false;
            deployAnimator.SetUIWrite(false);
        }
    }
}
