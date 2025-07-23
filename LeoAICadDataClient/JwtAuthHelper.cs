using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.IdentityModel.Tokens;

public class JwtAuthHelper
{
	public static bool ValidateJwtToken(string jwtToken, string apiKey, string projectId)
	{
		if (string.IsNullOrEmpty(jwtToken))
		{
			return false;
		}

		try
		{
			if (GetExpiryInSeconds(jwtToken) > 0)
			{
				return true;
			}
			else
			{
				return false;
			}
		}
		catch (SecurityTokenException ex)
		{
			Console.WriteLine($"Token validation failed: {ex.Message}");
			return false;
		}
	}

	public static long GetExpiryInSeconds(string jwtToken)
	{
		try
		{
			var tokenHandler = new JwtSecurityTokenHandler();
			var jsonToken = tokenHandler.ReadJwtToken(jwtToken);
			
			var expClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "exp");
			if (expClaim != null && long.TryParse(expClaim.Value, out long exp))
			{
				var expiryDateTime = DateTimeOffset.FromUnixTimeSeconds(exp);
				var currentDateTime = DateTimeOffset.UtcNow;
				
				return (long)(expiryDateTime - currentDateTime).TotalSeconds;
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error reading token expiry: {ex.Message}");
		}
		
		return 0;
	}
}