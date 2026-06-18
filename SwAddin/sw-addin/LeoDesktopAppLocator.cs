using System;
using System.Collections.Generic;
using System.IO;

namespace sw_addin
{
	/// <summary>
	/// Resolves the install location of the Leo desktop (Electron) application.
	/// </summary>
	/// <remarks>
	/// The desktop app ships as an electron-builder NSIS installer configured with
	/// <c>perMachine: false</c> (see leo-agents <c>electron-app/builder-configs/*.json</c>),
	/// and its NSIS script defaults the install-mode page to "Only for me". A default
	/// install therefore lands in the per-user location
	/// <c>%LOCALAPPDATA%\Programs\Leo\Leo.exe</c>, NOT <c>C:\Program Files\Leo\Leo.exe</c>.
	/// Users who choose "Anyone who uses this computer" get the all-users location under
	/// Program Files. The installer does not let users pick a custom directory
	/// (<c>allowToChangeInstallationDirectory</c> is unset), so these well-known roots are
	/// exhaustive. Probing all of them lets "Turn Leo On" launch the app regardless of
	/// install mode, instead of failing with a cryptic
	/// "The system cannot find the file specified" when only Program Files was checked.
	/// </remarks>
	public static class LeoDesktopAppLocator
	{
		/// <summary>File name of the Leo desktop executable (electron-builder productName "Leo").</summary>
		public const string ExecutableName = "Leo.exe";

		/// <summary>Folder name the desktop app installer creates under each install root.</summary>
		private const string InstallFolderName = "Leo";

		/// <summary>
		/// Public download link for the latest stable Leo desktop installer for Windows —
		/// the same URL the web app's "Download Leo for Windows" button uses
		/// (leo-agents cad-content <c>DOWNLOAD_APP_LINK</c>).
		/// </summary>
		public const string DownloadUrl =
			"https://storage.googleapis.com/installer-version-distribution-public/windows/latest/Leo.exe";

		/// <summary>
		/// Returns the candidate full paths to <c>Leo.exe</c>, ordered from the most common
		/// install location (per-user) to the least common. Roots that can't be resolved on
		/// this machine are skipped.
		/// </summary>
		public static IEnumerable<string> GetCandidatePaths()
		{
			// Per-user install (electron-builder default, perMachine:false):
			//   %LOCALAPPDATA%\Programs\Leo\Leo.exe
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (!string.IsNullOrEmpty(localAppData))
			{
				yield return Path.Combine(localAppData, "Programs", InstallFolderName, ExecutableName);
			}

			// All-users install ("Anyone who uses this computer"): %ProgramFiles%\Leo\Leo.exe
			string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			if (!string.IsNullOrEmpty(programFiles))
			{
				yield return Path.Combine(programFiles, InstallFolderName, ExecutableName);
			}

			// 32-bit Program Files, for safety where the process/OS view differs.
			string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			if (!string.IsNullOrEmpty(programFilesX86) &&
				!string.Equals(programFilesX86, programFiles, StringComparison.OrdinalIgnoreCase))
			{
				yield return Path.Combine(programFilesX86, InstallFolderName, ExecutableName);
			}
		}

		/// <summary>
		/// Resolves the full path to an installed <c>Leo.exe</c>, or <c>null</c> if the desktop
		/// app is not installed in any known location.
		/// </summary>
		/// <param name="fileExists">
		/// Existence probe; defaults to <see cref="File.Exists(string)"/>. Injectable for tests.
		/// </param>
		public static string ResolveInstalledPath(Func<string, bool> fileExists = null)
		{
			Func<string, bool> exists = fileExists ?? File.Exists;
			foreach (string candidate in GetCandidatePaths())
			{
				if (exists(candidate))
				{
					return candidate;
				}
			}

			return null;
		}
	}
}
