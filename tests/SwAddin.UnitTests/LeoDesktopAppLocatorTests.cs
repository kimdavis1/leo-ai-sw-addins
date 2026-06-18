using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using sw_addin;

namespace SwAddin.UnitTests
{
	/// <summary>
	/// Tests for <see cref="LeoDesktopAppLocator"/> — the resolver that lets "Turn Leo On"
	/// find the Leo desktop app whether it was installed per-user or for all users.
	/// Regression coverage for the per-user install (<c>%LOCALAPPDATA%\Programs\Leo</c>) that the
	/// old hard-coded <c>C:\Program Files\Leo\Leo.exe</c> path missed, which surfaced to users as
	/// "Error: The system cannot find the file specified".
	/// </summary>
	[TestClass]
	public class LeoDesktopAppLocatorTests
	{
		private static string PerUserPath =>
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Programs", "Leo", "Leo.exe");

		private static string AllUsersPath =>
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				"Leo", "Leo.exe");

		[TestMethod]
		public void GetCandidatePaths_IncludesPerUserAndAllUsersLocations()
		{
			List<string> candidates = LeoDesktopAppLocator.GetCandidatePaths().ToList();

			CollectionAssert.Contains(candidates, PerUserPath);
			CollectionAssert.Contains(candidates, AllUsersPath);
		}

		[TestMethod]
		public void GetCandidatePaths_ProbesPerUserBeforeAllUsers()
		{
			List<string> candidates = LeoDesktopAppLocator.GetCandidatePaths().ToList();

			int perUserIndex = candidates.IndexOf(PerUserPath);
			int allUsersIndex = candidates.IndexOf(AllUsersPath);

			Assert.IsTrue(perUserIndex >= 0 && allUsersIndex >= 0, "Both locations must be candidates.");
			Assert.IsTrue(
				perUserIndex < allUsersIndex,
				"Per-user install (the installer default) should be probed before the all-users location.");
		}

		[TestMethod]
		public void ResolveInstalledPath_FindsPerUserInstall()
		{
			// The bug: only C:\Program Files\Leo\Leo.exe was checked, so a per-user install
			// (the installer default) was never found — this is the case that regressed.
			string resolved = LeoDesktopAppLocator.ResolveInstalledPath(path => path == PerUserPath);

			Assert.AreEqual(PerUserPath, resolved);
		}

		[TestMethod]
		public void ResolveInstalledPath_FindsAllUsersInstall()
		{
			string resolved = LeoDesktopAppLocator.ResolveInstalledPath(path => path == AllUsersPath);

			Assert.AreEqual(AllUsersPath, resolved);
		}

		[TestMethod]
		public void ResolveInstalledPath_PrefersPerUserWhenBothExist()
		{
			string resolved = LeoDesktopAppLocator.ResolveInstalledPath(_ => true);

			Assert.AreEqual(PerUserPath, resolved);
		}

		[TestMethod]
		public void ResolveInstalledPath_ReturnsNullWhenNotInstalledAnywhere()
		{
			// Drives the "prompt the user to download" path in SwHelper.OpenElectronApp.
			string resolved = LeoDesktopAppLocator.ResolveInstalledPath(_ => false);

			Assert.IsNull(resolved);
		}

		[TestMethod]
		public void DownloadUrl_IsHttpsLeoInstaller()
		{
			StringAssert.StartsWith(LeoDesktopAppLocator.DownloadUrl, "https://");
			StringAssert.EndsWith(LeoDesktopAppLocator.DownloadUrl, "Leo.exe");
		}
	}
}
