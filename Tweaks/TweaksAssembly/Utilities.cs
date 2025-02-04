using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Assets.Scripts.Services;
using Assets.Scripts.Settings;
using Steamworks;
using UnityEngine;

public static class Utilities
{
	static Utilities()
	{
		SteamDirectory = FindSteamDirectory();
	}

	private static int activeSteamRequests;

	public static void EnsureSteamRequestManager()
	{
		if (SteamWorkshopRequestManager.Instance != null)
			return;

		new GameObject().AddComponent<SteamWorkshopRequestManager>();
	}

	private static string FindSteamDirectory()
	{
		// Mod folders
		var folders = AbstractServices.Instance.GetModFolders();
		if (folders.Count != 0)
		{
			return folders[0] + "/../../../../..";
		}

		// Relative to the game
		var relativePath = Path.GetFullPath("./../..");
		if (new DirectoryInfo(relativePath).Name == "steamapps")
		{
			return Path.GetFullPath("./../../..");
		}

		// Registry key
		using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
		{
			if (key?.GetValueNames().Contains("SteamPath") == true)
			{
				return key.GetValue("SteamPath")?.ToString().Replace('/', '\\') ?? string.Empty;
			}
		}

		// Guess common paths
		foreach (var path in new[]
		{
				@"Program Files (x86)\Steam",
				@"Program Files\Steam",
			})
		{
			foreach (var drive in Directory.GetLogicalDrives())
			{
				if (Directory.Exists(drive + path))
				{
					return drive + path;
				}
			}
		}

		foreach (var path in new[]
		{
				"/Library/Application Support/Steam",
				"/.steam/steam",
			})
		{
			var combinedPath = Environment.GetEnvironmentVariable("HOME") + path;
			if (Directory.Exists(combinedPath))
			{
				try
				{
					Process process = new Process
					{
						StartInfo = new ProcessStartInfo
						{
							WindowStyle = ProcessWindowStyle.Hidden,
							FileName = "readlink",
							Arguments = $"\"{combinedPath}\"",
							RedirectStandardOutput = true,
							UseShellExecute = false
						}
					};
					process.Start();

					var linkTarget = process.StandardOutput.ReadToEnd();
					if (!string.IsNullOrEmpty(linkTarget))
						return linkTarget;

					return combinedPath;
				}
				catch
				{
					return combinedPath;
				}
			}
		}

		return null;
	}

	public static string SteamDirectory;

	public static string SteamWorkshopDirectory => SteamDirectory == null ? null : Path.GetFullPath(new[] { SteamDirectory, "steamapps", "workshop", "content", "341800" }.Aggregate(Path.Combine));

	public static void SubscribeToMod(ulong modID, Action<EResult> callback)
	{
		EnsureSteamRequestManager();

		activeSteamRequests++;
		SteamWorkshopRequestManager.Instance.SubscribeToItem(new PublishedFileId_t(modID), (result) =>
		{
			installingSteamIDs.Add(modID);
			callback(result);
			activeSteamRequests--;
		});
	}

	public static void UnsubscribeFromMod(ulong modID, Action<EResult> callback)
	{
		EnsureSteamRequestManager();

		activeSteamRequests++;
		SteamWorkshopRequestManager.Instance.UnsubscribeFromItem(new PublishedFileId_t(modID), (result) =>
		{
			callback(result);
			activeSteamRequests--;
		});
	}

	public static void PopulateModInfoCache()
	{
		EnsureSteamRequestManager();

		activeSteamRequests++;
		SteamWorkshopRequestManager.Instance.PopulateCache((_) => activeSteamRequests--);
	}

	public static bool IsInstalled(ulong modID)
	{
		return Directory.Exists(Path.Combine(SteamWorkshopDirectory, modID.ToString()));
	}

	private static readonly HashSet<ulong> installingSteamIDs = new HashSet<ulong>();

	public static int GetInstallingMods()
	{
		return installingSteamIDs.Count(steamID => !IsInstalled(steamID));
	}

	public static WaitUntil WaitForSteamRequests() => new WaitUntil(() => activeSteamRequests == 0);

	private static readonly HashSet<string> disabledSteamIDs = new HashSet<string>();

	public static void DisableMod(string steamID) => disabledSteamIDs.Add(steamID);

	public static bool FlushDisabledMods()
	{
		if (SteamWorkshopDirectory == null)
			return false;

		var newMods = false;
		var disabledMods = ModSettingsManager.Instance.ModSettings.DisabledModPaths.ToList();

		foreach (var steamID in disabledSteamIDs)
		{
			var modPath = Path.Combine(SteamWorkshopDirectory, steamID);
			if (!disabledMods.Contains(modPath))
			{
				disabledMods.Add(modPath);
				newMods |= true;
			}
		}

		ModSettingsManager.Instance.ModSettings.DisabledModPaths = disabledMods.ToArray();
		ModSettingsManager.Instance.SaveModSettings();

		disabledSteamIDs.Clear();

		return newMods;
	}
}