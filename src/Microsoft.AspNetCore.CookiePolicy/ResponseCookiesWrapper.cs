﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.CookiePolicy
{
    internal class ResponseCookiesWrapper : IResponseCookies, ITrackingConsentFeature
    {
        private bool? _isConsentNeeded;
        private bool? _hasConsent;

        public ResponseCookiesWrapper(HttpContext context, CookiePolicyOptions options, IResponseCookiesFeature feature)
        {
            Context = context;
            Feature = feature;
            Options = options;
        }

        private HttpContext Context { get; }

        private IResponseCookiesFeature Feature { get; }

        private IResponseCookies Cookies => Feature.Cookies;

        private CookiePolicyOptions Options { get; }

        public bool IsConsentNeeded
        {
            get
            {
                if (!_isConsentNeeded.HasValue)
                {
                    _isConsentNeeded = Options.CheckConsentPolicyNeeded == null ? false
                        : Options.CheckConsentPolicyNeeded(Context);
                }

                return _isConsentNeeded.Value;
            }
        }

        public bool HasConsent
        {
            get
            {
                if (!_hasConsent.HasValue)
                {
                    var cookie = Context.Request.Cookies[Options.ConsentCookie.Name];
                    _hasConsent = string.Equals(cookie, "yes");
                }

                return _hasConsent.Value;
            }
        }

        public bool CanTrack => !IsConsentNeeded || HasConsent;

        public void GrantConsent()
        {
            if (!HasConsent && !Context.Response.HasStarted)
            {
                _hasConsent = true;
                var cookieOptions = Options.ConsentCookie.Build(Context);
                // Note policy will be applied. What purpose should be used?
                // We don't want to bypass policy because we want HttpOnly, Secure, etc. to apply.
                Append(Options.ConsentCookie.Name, "yes", cookieOptions);
            }
        }

        public void WithdrawConsent()
        {
            if (CanTrack && !Context.Response.HasStarted)
            {
                var cookieOptions = Options.ConsentCookie.Build(Context);
                Delete(Options.ConsentCookie.Name, cookieOptions);
            }
            _hasConsent = false;
        }

        private bool CheckPolicyRequired()
        {
            return !CanTrack
                || Options.MinimumSameSitePolicy != SameSiteMode.None
                || Options.HttpOnly != HttpOnlyPolicy.None
                || Options.Secure != CookieSecurePolicy.None;
        }

        public void Append(string key, string value)
        {
            if (CheckPolicyRequired() || Options.OnAppendCookie != null)
            {
                Append(key, value, new CookieOptions());
            }
            else
            {
                Cookies.Append(key, value);
            }
        }

        public void Append(string key, string value, CookieOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var canIssueCookie = CanTrack;
            // TODO: Default persist policy?
            ApplyPolicy(options);
            if (Options.OnAppendCookie != null)
            {
                var context = new AppendCookieContext(Context, options, key, value)
                {
                    IsConsentNeeded = IsConsentNeeded,
                    HasConsent = HasConsent,
                    CanIssueCookie = canIssueCookie,
                };
                Options.OnAppendCookie(context);

                key = context.CookieName;
                value = context.CookieValue;
                canIssueCookie = context.CanIssueCookie;
            }

            if (canIssueCookie)
            {
                Cookies.Append(key, value, options);
            }
        }

        public void Delete(string key)
        {
            if (CheckPolicyRequired() || Options.OnDeleteCookie != null)
            {
                Delete(key, new CookieOptions());
            }
            else
            {
                Cookies.Delete(key);
            }
        }

        public void Delete(string key, CookieOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // Assume you can always delete cookies unless directly overridden in the user event.
            var canIssueCookie = true;
            // TODO: Default persist policy?
            ApplyPolicy(options);
            if (Options.OnDeleteCookie != null)
            {
                var context = new DeleteCookieContext(Context, options, key)
                {
                    IsConsentNeeded = IsConsentNeeded,
                    HasConsent = HasConsent,
                    CanIssueCookie = canIssueCookie,
                };
                Options.OnDeleteCookie(context);

                key = context.CookieName;
                canIssueCookie = context.CanIssueCookie;
            }

            if (canIssueCookie)
            {
                Cookies.Delete(key, options);
            }
        }

        private void ApplyPolicy(CookieOptions options)
        {
            switch (Options.Secure)
            {
                case CookieSecurePolicy.Always:
                    options.Secure = true;
                    break;
                case CookieSecurePolicy.SameAsRequest:
                    options.Secure = Context.Request.IsHttps;
                    break;
                case CookieSecurePolicy.None:
                    break;
                default:
                    throw new InvalidOperationException();
            }
            switch (Options.MinimumSameSitePolicy)
            {
                case SameSiteMode.None:
                    break;
                case SameSiteMode.Lax:
                    if (options.SameSite == SameSiteMode.None)
                    {
                        options.SameSite = SameSiteMode.Lax;
                    }
                    break;
                case SameSiteMode.Strict:
                    options.SameSite = SameSiteMode.Strict;
                    break;
                default:
                    throw new InvalidOperationException($"Unrecognized {nameof(SameSiteMode)} value {Options.MinimumSameSitePolicy.ToString()}");
            }
            switch (Options.HttpOnly)
            {
                case HttpOnlyPolicy.Always:
                    options.HttpOnly = true;
                    break;
                case HttpOnlyPolicy.None:
                    break;
                default:
                    throw new InvalidOperationException($"Unrecognized {nameof(HttpOnlyPolicy)} value {Options.HttpOnly.ToString()}");
            }
        }
    }
}