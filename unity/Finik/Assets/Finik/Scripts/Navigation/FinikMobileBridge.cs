using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Finik.Navigation
{
    public static class FinikMobileBridge
    {
        [Serializable]
        sealed class InteractionEventPayload
        {
            public string eventId;
            public string type;
            public string interactionId;
        }

#if FINIK_REACT_NATIVE_HOST && UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void sendMessageToMobileApp(string message);
#endif

        /// <summary>
        /// True only for the legacy build where Unity is intentionally embedded inside the React Native host.
        /// The standalone Finik app is pure Unity and must never probe React classes on Android/iOS.
        /// </summary>
#if FINIK_REACT_NATIVE_HOST && (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        public static bool HasHost => true;
#else
        public static bool HasHost => false;
#endif

        public static void SendObjectTapped(string interactionId) => SendInteraction("objectTapped", interactionId);

        public static void SendPetTapped() => SendInteraction("petTapped", "pet");

        static void SendInteraction(string type, string interactionId)
        {
            var payload = new InteractionEventPayload
            {
                eventId = Guid.NewGuid().ToString("N"),
                type = type,
                interactionId = interactionId
            };
            Send(JsonUtility.ToJson(payload));
        }

        static void Send(string message)
        {
#if FINIK_REACT_NATIVE_HOST && UNITY_ANDROID && !UNITY_EDITOR
            using var bridge = new AndroidJavaClass("com.azesmwayreactnativeunity.ReactNativeUnityViewManager");
            bridge.CallStatic("sendMessageToMobileApp", message);
#elif FINIK_REACT_NATIVE_HOST && UNITY_IOS && !UNITY_EDITOR
            sendMessageToMobileApp(message);
#else
            // Standalone app: Unity owns all UI and interaction routing. Keep the event visible in logs only.
            Debug.Log("FINIK_UNITY_MESSAGE=" + message);
#endif
        }
    }
}
