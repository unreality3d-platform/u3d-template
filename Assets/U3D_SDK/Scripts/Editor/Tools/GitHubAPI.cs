using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace U3D.Editor
{
    public static class GitHubAPI
    {
        private const string GITHUB_API_BASE = "https://api.github.com";
        private const string TEMPLATE_REPO_OWNER = "unreality3d-platform";
        private const string TEMPLATE_REPO_NAME = "u3d-sdk-template";

        private static async Task<bool> CheckRateLimit()
        {
            if (!GitHubTokenManager.HasValidToken)
            {
                return false;
            }

            try
            {
                using (var client = CreateAuthenticatedClient())
                {
                    var response = await client.GetAsync($"{GITHUB_API_BASE}/rate_limit");
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var rateLimit = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);
                        var resources = JsonConvert.DeserializeObject<Dictionary<string, object>>(rateLimit["resources"].ToString());
                        var core = JsonConvert.DeserializeObject<Dictionary<string, object>>(resources["core"].ToString());

                        var remaining = int.Parse(core["remaining"].ToString());
                        var resetTime = long.Parse(core["reset"].ToString());

                        if (remaining < 5)
                        {
                            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            var waitTime = resetTime - currentTime;

                            Debug.LogWarning($"GitHub API rate limit low: {remaining} requests remaining. Reset in {waitTime} seconds.");

                            if (waitTime > 300) // Don't wait more than 5 minutes
                            {
                                return false;
                            }
                        }

                        return remaining > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Rate limit check failed: {ex.Message}");
            }

            return true; // Assume OK if check fails
        }

        public static async Task<GitHubRepositoryResult> CopyFromTemplate(string newRepositoryName, string description = "")
        {
            if (!GitHubTokenManager.HasValidToken)
            {
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = "No valid GitHub token available"
                };
            }

            // Check rate limit before making API call
            if (!await CheckRateLimit())
            {
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = "GitHub API rate limit exceeded. Please wait and try again."
                };
            }

            try
            {
                using (var client = CreateAuthenticatedClient())
                {
                    // Add timeout for long operations
                    client.Timeout = TimeSpan.FromMinutes(2);

                    var templateData = new
                    {
                        name = newRepositoryName,
                        description = string.IsNullOrEmpty(description) ? $"Unity project created with Unreality3D SDK" : description,
                        @private = false,
                        include_all_branches = false
                    };

                    var json = JsonConvert.SerializeObject(templateData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var templateUrl = $"{GITHUB_API_BASE}/repos/{TEMPLATE_REPO_OWNER}/{TEMPLATE_REPO_NAME}/generate";
                    var response = await client.PostAsync(templateUrl, content);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);

                        // Wait a moment for GitHub to finish repository setup
                        await Task.Delay(2000);

                        return new GitHubRepositoryResult
                        {
                            Success = true,
                            RepositoryName = newRepositoryName,
                            FullName = result.ContainsKey("full_name") ? result["full_name"].ToString() : $"{GitHubTokenManager.GitHubUsername}/{newRepositoryName}",
                            CloneUrl = result.ContainsKey("clone_url") ? result["clone_url"].ToString() : "",
                            SshUrl = result.ContainsKey("ssh_url") ? result["ssh_url"].ToString() : "",
                            HtmlUrl = result.ContainsKey("html_url") ? result["html_url"].ToString() : "",
                            Message = "Repository created from template successfully"
                        };
                    }
                    else
                    {
                        var errorMessage = await ParseGitHubError(responseText);
                        return new GitHubRepositoryResult
                        {
                            Success = false,
                            ErrorMessage = errorMessage
                        };
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = "GitHub repository creation timed out. The repository may still be created - check your GitHub account."
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"GitHub template copy error: {ex.Message}");
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = $"Template copy failed: {ex.Message}"
                };
            }
        }

        public static async Task<GitHubRepositoryResult> CreateRepository(string repositoryName, string description = "")
        {
            if (!GitHubTokenManager.HasValidToken)
            {
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = "No valid GitHub token available"
                };
            }

            try
            {
                using (var client = CreateAuthenticatedClient())
                {
                    var repoData = new
                    {
                        name = repositoryName,
                        description = string.IsNullOrEmpty(description) ? $"Unity project created with Unreality3D SDK" : description,
                        @private = false,
                        has_issues = true,
                        has_projects = false,
                        has_wiki = false,
                        auto_init = false
                    };

                    var json = JsonConvert.SerializeObject(repoData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync($"{GITHUB_API_BASE}/user/repos", content);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);

                        return new GitHubRepositoryResult
                        {
                            Success = true,
                            RepositoryName = repositoryName,
                            FullName = result.ContainsKey("full_name") ? result["full_name"].ToString() : $"{GitHubTokenManager.GitHubUsername}/{repositoryName}",
                            CloneUrl = result.ContainsKey("clone_url") ? result["clone_url"].ToString() : "",
                            SshUrl = result.ContainsKey("ssh_url") ? result["ssh_url"].ToString() : "",
                            HtmlUrl = result.ContainsKey("html_url") ? result["html_url"].ToString() : "",
                            Message = "Repository created successfully"
                        };
                    }
                    else
                    {
                        var errorMessage = await ParseGitHubError(responseText);
                        return new GitHubRepositoryResult
                        {
                            Success = false,
                            ErrorMessage = errorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"GitHub repository creation error: {ex.Message}");
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = $"Repository creation failed: {ex.Message}"
                };
            }
        }

        public static async Task<bool> CheckRepositoryExists(string repositoryName)
        {
            if (!GitHubTokenManager.HasValidToken)
            {
                return false;
            }

            try
            {
                using (var client = CreateAuthenticatedClient())
                {
                    var response = await client.GetAsync($"{GITHUB_API_BASE}/repos/{GitHubTokenManager.GitHubUsername}/{repositoryName}");
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> GenerateUniqueRepositoryName(string baseName)
        {
            var safeName = SanitizeRepositoryName(baseName);
            var uniqueName = safeName;
            var counter = 1;

            while (await CheckRepositoryExists(uniqueName))
            {
                uniqueName = $"{safeName}-{counter}";
                counter++;

                if (counter > 100) // Prevent infinite loop
                {
                    uniqueName = $"{safeName}-{DateTime.Now.Ticks}";
                    break;
                }
            }

            return uniqueName;
        }

        public static string SanitizeRepositoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unity-project";
            }

            // Convert to lowercase and replace spaces/special chars with hyphens
            var sanitized = name.ToLower()
                .Replace(" ", "-")
                .Replace("_", "-")
                .Replace(".", "-");

            // Remove invalid characters
            var validChars = "abcdefghijklmnopqrstuvwxyz0123456789-";
            var result = new StringBuilder();

            foreach (char c in sanitized)
            {
                if (validChars.Contains(c))
                {
                    result.Append(c);
                }
            }

            var finalName = result.ToString().Trim('-');

            // Ensure it starts with a letter or number
            if (finalName.Length > 0 && finalName[0] == '-')
            {
                finalName = finalName.Substring(1);
            }

            // Ensure minimum length
            if (finalName.Length < 3)
            {
                finalName = "unity-project";
            }

            // Ensure maximum length (GitHub limit is 100 chars)
            if (finalName.Length > 80)
            {
                finalName = finalName.Substring(0, 80).TrimEnd('-');
            }

            return finalName;
        }

        // NEW METHOD: Create fresh repository via GitHub API (not from template)
        public static async Task<GitHubRepositoryResult> CreateFreshRepository(string repositoryName, string description)
        {
            if (!GitHubTokenManager.HasValidToken)
            {
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = "No valid GitHub token available"
                };
            }

            // Check rate limit before making API call
            if (!await CheckRateLimit())
            {
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = "GitHub API rate limit exceeded. Please wait and try again."
                };
            }

            try
            {
                using (var client = CreateAuthenticatedClient())
                {
                    var repoData = new
                    {
                        name = repositoryName,
                        description = string.IsNullOrEmpty(description) ? $"Unity WebGL project created with Unreality3D SDK" : description,
                        @private = false, // Public repository for GitHub Pages
                        auto_init = false, // We'll upload files ourselves
                        has_issues = true,
                        has_projects = false,
                        has_wiki = false
                    };

                    var json = JsonConvert.SerializeObject(repoData);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync($"{GITHUB_API_BASE}/user/repos", content);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);

                        return new GitHubRepositoryResult
                        {
                            Success = true,
                            RepositoryName = repositoryName,
                            FullName = result.ContainsKey("full_name") ? result["full_name"].ToString() : $"{GitHubTokenManager.GitHubUsername}/{repositoryName}",
                            CloneUrl = result.ContainsKey("clone_url") ? result["clone_url"].ToString() : "",
                            SshUrl = result.ContainsKey("ssh_url") ? result["ssh_url"].ToString() : "",
                            HtmlUrl = result.ContainsKey("html_url") ? result["html_url"].ToString() : "",
                            Message = "Repository created successfully"
                        };
                    }
                    else
                    {
                        var errorMessage = await ParseGitHubError(responseText);
                        return new GitHubRepositoryResult
                        {
                            Success = false,
                            ErrorMessage = errorMessage
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"GitHub repository creation error: {ex.Message}");
                return new GitHubRepositoryResult
                {
                    Success = false,
                    ErrorMessage = $"Repository creation failed: {ex.Message}"
                };
            }
        }

        // NEW METHOD: Upload file content via GitHub API
        public static async Task<GitOperationResult> UploadFileContent(string repositoryName, string filePath, string base64Content, string commitMessage)
        {
            if (!GitHubTokenManager.HasValidToken)
            {
                return new GitOperationResult
                {
                    Success = false,
                    ErrorMessage = "No valid GitHub token available"
                };
            }

            try
            {
                using (var client = CreateAuthenticatedClient())
                {
                    var uploadRequest = new
                    {
                        message = commitMessage,
                        content = base64Content,
                        branch = "main"
                    };

                    var json = JsonConvert.SerializeObject(uploadRequest);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var url = $"{GITHUB_API_BASE}/repos/{GitHubTokenManager.GitHubUsername}/{repositoryName}/contents/{filePath}";
                    var response = await client.PutAsync(url, content);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        return new GitOperationResult
                        {
                            Success = true,
                            Message = $"File uploaded successfully: {filePath}"
                        };
                    }
                    else
                    {
                        var errorMessage = await ParseGitHubError(responseText);
                        return new GitOperationResult
                        {
                            Success = false,
                            ErrorMessage = $"Failed to upload {filePath}: {errorMessage}"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"GitHub file upload error: {ex.Message}");
                return new GitOperationResult
                {
                    Success = false,
                    ErrorMessage = $"File upload failed: {ex.Message}"
                };
            }
        }

        // NEW METHOD: Enable GitHub Pages
        public static async Task<GitOperationResult> EnableGitHubPages(string repositoryName)
        {
            if (!GitHubTokenManager.HasValidToken)
            {
                return new GitOperationResult
                {
                    Success = false,
                    ErrorMessage = "No valid GitHub token available"
                };
            }

            try
            {
                using (var client = CreateAuthenticatedClient())
                {
                    var pagesRequest = new
                    {
                        source = new
                        {
                            branch = "main",
                            path = "/"
                        }
                    };

                    var json = JsonConvert.SerializeObject(pagesRequest);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var url = $"{GITHUB_API_BASE}/repos/{GitHubTokenManager.GitHubUsername}/{repositoryName}/pages";
                    var response = await client.PostAsync(url, content);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        // Success or already exists (409 Conflict is normal if Pages already enabled)
                        return new GitOperationResult
                        {
                            Success = true,
                            Message = "GitHub Pages enabled successfully"
                        };
                    }
                    else
                    {
                        var errorMessage = await ParseGitHubError(responseText);
                        return new GitOperationResult
                        {
                            Success = false,
                            ErrorMessage = $"Failed to enable GitHub Pages: {errorMessage}"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"GitHub Pages setup error: {ex.Message}");
                return new GitOperationResult
                {
                    Success = false,
                    ErrorMessage = $"GitHub Pages setup failed: {ex.Message}"
                };
            }
        }

        private static HttpClient CreateAuthenticatedClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"token {GitHubTokenManager.Token}");
            client.DefaultRequestHeaders.Add("User-Agent", "Unreality3D-Unity-SDK");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        private static async Task<string> ParseGitHubError(string responseText)
        {
            try
            {
                var errorData = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);

                if (errorData.ContainsKey("message"))
                {
                    var message = errorData["message"].ToString();

                    // Handle common GitHub error messages
                    if (message.Contains("name already exists"))
                    {
                        return "Repository name already exists. Please try a different name.";
                    }
                    else if (message.Contains("Bad credentials"))
                    {
                        return "Invalid GitHub token. Please check your token and try again.";
                    }
                    else if (message.Contains("rate limit"))
                    {
                        return "GitHub API rate limit exceeded. Please wait a moment and try again.";
                    }

                    return message;
                }

                if (errorData.ContainsKey("errors"))
                {
                    var errors = JsonConvert.DeserializeObject<object[]>(errorData["errors"].ToString());
                    if (errors.Length > 0)
                    {
                        var firstError = JsonConvert.DeserializeObject<Dictionary<string, object>>(errors[0].ToString());
                        if (firstError.ContainsKey("message"))
                        {
                            return firstError["message"].ToString();
                        }
                    }
                }
            }
            catch
            {
                // If we can't parse the error, return the raw response
            }

            return $"GitHub API error: {responseText}";
        }
    }

    [System.Serializable]
    public class GitHubRepositoryResult
    {
        public bool Success { get; set; }
        public string RepositoryName { get; set; }
        public string FullName { get; set; }
        public string CloneUrl { get; set; }
        public string SshUrl { get; set; }
        public string HtmlUrl { get; set; }
        public string Message { get; set; }
        public string ErrorMessage { get; set; }
    }

    [System.Serializable]
    public class GitHubRepositoryResponse
    {
        public string name;
        public string full_name;
        public string clone_url;
        public string html_url;
    }
}