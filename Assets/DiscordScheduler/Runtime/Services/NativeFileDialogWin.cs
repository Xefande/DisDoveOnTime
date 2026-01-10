using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace DiscordScheduler
{
    internal static class NativeFileDialogWin
    {
    #if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const string DllName = "DiscordScheduler.NativeDialogs";

        [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int OpenFileDialogW(string filterSpec, StringBuilder outPath, int outPathChars);
    #endif

        public static string OpenFile(string filterSpec)
        {

        #if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var sb = new StringBuilder(4096);
                int ok = OpenFileDialogW(filterSpec, sb, sb.Capacity);
                if (ok == 1)
                    return sb.ToString();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Native file dialog failed: " + e.Message);
            }
        #endif

            return null;
        }

        // Helper to build the Win32-style double-NUL filter string.
        public static string BuildFilter(params (string name, string spec)[] items)
        {
            // Format: "Name\0Spec\0Name2\0Spec2\0\0"
            var sb = new StringBuilder();
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    sb.Append(items[i].name ?? string.Empty).Append('\0');
                    sb.Append(items[i].spec ?? "*.*").Append('\0');
                }
            }
            sb.Append('\0');
            return sb.ToString();
        }
    }
}