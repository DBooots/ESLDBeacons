using System;
using System.Collections.Generic;
using UnityEngine;

namespace Booots_Unity_Extensions
{
    public class AnimationEventExtension : MonoBehaviour
    {
        private List<Action> actions = new List<Action>();
        private Animation animation;
        
        public static AnimationEventExtension AddOrGetAnimationEventExtension(Animation animation)
        {
            AnimationEventExtension value =  animation.gameObject.AddOrGetComponent<AnimationEventExtension>();
            value.animation = animation;
            return value;
        }

        public void AddEvent(AnimationClip clip, float time, Action callback)
        {
            if (animation.GetClip(clip.name) == null)
                throw new ArgumentException("Clip is not a member of this Animation.");
            actions.Add(callback);
            clip.AddEvent(new AnimationEvent()
            {
                time = time,
                intParameter = actions.Count - 1,
                functionName = "OnEvent"
            });
        }

        public void OnEvent(int index)
        {
            actions[index]();
        }
    }
}
