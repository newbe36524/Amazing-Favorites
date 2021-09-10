﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Newbe.BookmarkManager.Services;
using Newbe.BookmarkManager.Services.EventHubs;
using WebExtensions.Net.Omnibox;
using WebExtensions.Net.Tabs;

namespace Newbe.BookmarkManager.Pages
{
    public partial class Background
    {
        [Inject] public IJSRuntime JsRuntime { get; set; } = null!;
        [Inject] public IUserOptionsService UserOptionsService { get; set; } = null!;
        [Inject] public IJobHost JobHost { get; set; }
        
        [Inject] public IBkSearcher BkSearcher { get; set; }
        
        [Inject]
        public IAfEventHub AfEventHub { get; set; }

        private UserOptions _userOptions = null!;

        private void OnReceivedCommand(string command)
        {
            if (command == Consts.Commands.OpenManager)
            {
                WebExtensions.Tabs.ActiveOrOpenManagerAsync();
            }
        }
        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            _userOptions = await UserOptionsService.GetOptionsAsync();
            await WebExtensions.Commands.OnCommand.AddListener(OnReceivedCommand);
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);
            if (firstRender)
            {
                _moduleLoader = new JsModuleLoader(JsRuntime);
                await _moduleLoader.LoadAsync("/content/background_keyboard.js");
                var userOptions = await UserOptionsService.GetOptionsAsync();
                if (userOptions is
                    {
                        AcceptPrivacyAgreement: true,
                        ApplicationInsightFeature:
                        {
                            Enabled: true
                        }
                    })
                {
                    await _moduleLoader.LoadAsync("/content/ai.js");
                }

                if (userOptions?.OmniboxSuggestFeature?.Enabled == true)
                {
                    await AddOmniBoxSuggestAsync();
                }
                AfEventHub.RegisterHandler<UserOptionSaveEvent>(HandleUserOptionSaveEvent);
                var lDotNetReference = DotNetObjectReference.Create(this);
                await JsRuntime.InvokeVoidAsync("DotNet.SetDotnetReference", lDotNetReference);
                await JobHost.StartAsync();
                
            }
        }
        
        private Task HandleUserOptionSaveEvent(UserOptionSaveEvent evt)
        {
            if (evt.OminiboxSuggestChanged == false)
            {
                return Task.CompletedTask;
            }
            
            if (evt?.UserOptions?.OmniboxSuggestFeature?.Enabled == true)
            {
                return InvokeAsync(async () =>
                {
                    Logger.LogInformation("addOmniBoxSuggest");
                    await AddOmniBoxSuggestAsync();
                });
            }

            return InvokeAsync(async () =>
            {
                Logger.LogInformation(" removeOmniBoxSuggest");
                await RemoveOmniBoxSuggestAsync();
            });
        }
        public async Task<SuggestResult[]> GetOmniBoxSuggest(string input)
        {
            var option = (await UserOptionsService.GetOptionsAsync())?.OmniboxSuggestFeature;
            if (option == null || option.Enabled == false)
            {
                return Array.Empty<SuggestResult>();
            }
            var searchResult = await BkSearcher.Search(input, option.SuggestCount);
            var suggestResults = searchResult.Select(a => new SuggestResult
            {
                Content = a.Bk.Url,
                Description = a.Bk.Title
            }).ToArray();

            return suggestResults;

        }
        public async Task AddOmniBoxSuggestAsync()
        {
            await WebExtensions.Omnibox.OnInputChanged.AddListener(OmniboxSuggestActiveAsync);
            await WebExtensions.Omnibox.OnInputEntered.AddListener(OmniboxSuggestTabOpenAsync);
        }

        public async Task RemoveOmniBoxSuggestAsync()
        {
            await WebExtensions.Omnibox.OnInputChanged.RemoveListener(OmniboxSuggestActiveAsync);
            await WebExtensions.Omnibox.OnInputEntered.RemoveListener(OmniboxSuggestTabOpenAsync);
        }
        
        async void OmniboxSuggestActiveAsync(string input, Action<IEnumerable<SuggestResult>> suggest)
        {
            var result = await GetOmniBoxSuggest(input);
            suggest(result);
        }
        async void OmniboxSuggestTabOpenAsync(string url, OnInputEnteredDisposition disposition)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                var managerTabTitle = Consts.AppName;
                var managerTabs = await WebExtensions.Tabs.Query(new QueryInfo {Title = managerTabTitle});
                if (managerTabs.Any())
                {
                    await WebExtensions.Tabs.Update(managerTabs.FirstOrDefault().Id, new UpdateProperties {Active = true});
                }
                else
                {
                    await WebExtensions.Tabs.Create(new CreateProperties {Url = "/Manager/index.html"});
                }

                return;
            }

            switch (disposition)
            {
                case OnInputEnteredDisposition.CurrentTab:
                    await WebExtensions.Tabs.Update(tabId: null, updateProperties: new UpdateProperties {Url = url});
                    break;
                case OnInputEnteredDisposition.NewForegroundTab:
                    await WebExtensions.Tabs.Create(new CreateProperties {Url = url, Active = true});
                    break;
                case OnInputEnteredDisposition.NewBackgroundTab:
                    await WebExtensions.Tabs.Create(new CreateProperties {Url = url, Active = false});
                    break;
                default:
                    break;
            }
        }
    }
}