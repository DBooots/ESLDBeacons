using UnityEngine;

namespace Booots_Unity_Extensions
{
    public class RotatingTransform : MonoBehaviour
    {

        public Vector3 rotationSpeed = new Vector3(0, 0, 0);

        // Update is called once per frame
        void Update()
        {
            if (Time.timeScale > 0)
                transform.Rotate(rotationSpeed * Time.deltaTime);
        }
    }
}