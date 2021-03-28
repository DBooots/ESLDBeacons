using System;
using System.Collections;
using UnityEngine;

namespace Booots_Unity_Extensions
{
    public class RotatingParticleController : PartModule
    {

        public KSPParticleEmitter emitter;
        public RotatingTransform rotatingTransform;

        [KSPField]
        public string gameObjectPath;
        [KSPField]
        public Vector3 rotationSpeed;

        private Coroutine delayedStop;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            
            Transform currentTransform = gameObject.transform;
            currentTransform = currentTransform.GetChild(0).GetChild(0);

            if (gameObjectPath != "")
            {
                string[] gameObjects = gameObjectPath.Split('/', '\\');
                foreach (string objName in gameObjects)
                {
                    for (int i = currentTransform.childCount - 1; i >= -1; i--)
                    {
                        if (i == -1)
                        {
                            Debug.LogErrorFormat("Could not find GameObject {0} in {1}.", objName, currentTransform.name);
                            this.enabled = false;
                            return;
                        }
                        if (currentTransform.GetChild(i).name == objName)
                        {
                            currentTransform = currentTransform.GetChild(i);
                            break;
                        }
                    }
                }
            }
            GameObject targetObject = currentTransform.gameObject;

            emitter = targetObject.GetComponent<KSPParticleEmitter>();
            if (emitter == null)
            {
                Debug.LogErrorFormat("Could not find a KSPParticleEmitter on {0}.", targetObject.name);
                this.enabled = false;
                return;
            }

            rotatingTransform = targetObject.GetComponent<RotatingTransform>();
            if (rotatingTransform == null)
            {
                rotatingTransform = targetObject.AddComponent<RotatingTransform>();
                rotatingTransform.rotationSpeed = this.rotationSpeed;
                rotatingTransform.enabled = emitter.emit;
            }
        }

        public void ToggleEmit()
        {
            if (!enabled)
                return;

            if (emitter.emit)
                StopEmit();
            else
                StartEmit();
        }

        public void StopEmit()
        {
            if (!enabled)
                return;
            if (emitter.emit == false && !rotatingTransform.enabled)
                return;
            emitter.emit = false;
            delayedStop = StartCoroutine(StopRotatingCoroutine());
        }
        public void StartEmit()
        {
            if (!enabled)
                return;

            if (emitter.emit == true && rotatingTransform.enabled)
                return;
            emitter.emit = true;
            rotatingTransform.enabled = true;
            if (delayedStop != null)
                StopCoroutine(delayedStop);
        }

        public IEnumerator StopRotatingCoroutine()
        {
            yield return new WaitForSeconds(emitter.maxEnergy);
            rotatingTransform.enabled = false;
            delayedStop = null;
        }
    }
}