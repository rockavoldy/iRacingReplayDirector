using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace iRacingReplayDirector.AI.LLM
{
	/// <summary>
	/// Provider for any service exposing an OpenAI-compatible chat completions API
	/// (e.g. OpenRouter, Groq, Together, LM Studio, vLLM). The user supplies the base
	/// URL and optional API key, and available models are queried from the API itself.
	/// </summary>
	public class OpenAICompatibleProvider : LLMProviderBase
	{
		private const string DEFAULT_BASE_URL = "https://api.openai.com/v1";

		private string _baseUrl = DEFAULT_BASE_URL;

		public string BaseUrl
		{
			get { return _baseUrl; }
			set { _baseUrl = value; }
		}

		public string ApiKey { get; set; }

		public string Model { get; set; }

		public override string Name => "OpenAI Compatible";

		public override string ModelName => Model;

		public override bool IsConfigured =>
			!string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);

		public override bool RequiresApiKey => false;

		protected override string Endpoint => NormalizeBaseUrl() + "/chat/completions";

		// Most OpenAI-compatible servers support JSON mode; harmlessly ignored elsewhere.
		protected override bool SupportsJsonResponseFormat => true;

		protected override string GetAuthorizationHeader()
		{
			return string.IsNullOrWhiteSpace(ApiKey) ? null : $"Bearer {ApiKey}";
		}

		/// <summary>
		/// Fetch the list of available model IDs from the API's /models endpoint.
		/// Throws on failure so the caller can surface an error to the user.
		/// </summary>
		public async Task<List<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(BaseUrl))
				throw new InvalidOperationException("Base URL is required to fetch models.");

			var models = new List<string>();

			using (var request = new HttpRequestMessage(HttpMethod.Get, NormalizeBaseUrl() + "/models"))
			{
				string authHeader = GetAuthorizationHeader();
				if (!string.IsNullOrEmpty(authHeader))
				{
					request.Headers.TryAddWithoutValidation("Authorization", authHeader);
				}

				using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					cts.CancelAfter(TimeSpan.FromSeconds(30));
					var response = await SharedHttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

					string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

					if (!response.IsSuccessStatusCode)
					{
						string errorMessage = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
						try
						{
							var errorObj = JObject.Parse(responseJson);
							var apiError = errorObj["error"]?["message"]?.ToString();
							if (!string.IsNullOrEmpty(apiError))
								errorMessage = apiError;
						}
						catch { }
						throw new HttpRequestException(errorMessage);
					}

					ParseModels(responseJson, models);
				}
			}

			return models;
		}

		private static void ParseModels(string responseJson, List<string> models)
		{
			JToken root = JToken.Parse(responseJson);

			// Standard OpenAI-compatible format: { "data": [ { "id": "gpt-4o" }, ... ] }
			// Some servers return a bare array of model objects instead.
			var dataArray = root["data"] as JArray ?? root as JArray;

			if (dataArray == null)
				return;

			foreach (var entry in dataArray)
			{
				string id = entry is JObject obj ? obj["id"]?.ToString() : entry?.ToString();
				if (!string.IsNullOrWhiteSpace(id) && !models.Contains(id))
				{
					models.Add(id);
				}
			}
		}

		/// <summary>
		/// Normalize the user-supplied base URL so we can append standard API paths.
		/// Accepts values with or without a trailing slash, with or without a version
		/// segment, and with or without a full endpoint path already appended.
		/// </summary>
		private string NormalizeBaseUrl()
		{
			string url = (BaseUrl ?? string.Empty).Trim().TrimEnd('/');

			// If the user pasted the full chat completions endpoint, strip it back to a base.
			const string chatSuffix = "/chat/completions";
			if (url.EndsWith(chatSuffix, StringComparison.OrdinalIgnoreCase))
			{
				url = url.Substring(0, url.Length - chatSuffix.Length);
			}

			return url;
		}
	}
}
