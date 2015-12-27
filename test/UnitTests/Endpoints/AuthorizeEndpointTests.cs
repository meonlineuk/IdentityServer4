﻿/*
 * Copyright 2014, 2015 Dominick Baier, Brock Allen
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using FluentAssertions;
using IdentityServer4.Core.Endpoints;
using System.Threading.Tasks;
using Xunit;
using UnitTests.Common;
using IdentityServer4.Core.Hosting;
using Microsoft.AspNet.Http.Internal;
using IdentityServer4.Core.Configuration;
using System;
using System.Collections.Specialized;
using System.Security.Claims;
using IdentityServer4.Core.Validation;
using IdentityServer4.Core.Results;
using IdentityServer4.Core.Services;
using Microsoft.Extensions.Logging;
using IdentityServer4.Core.Events;
using IdentityServer4.Core.Models;

namespace IdentityServer4.Tests.Endpoints
{
    public class AuthorizeEndpointTests
    {
        const string Category = "Authorize Endpoint";

        AuthorizeEndpoint _subject;

        public AuthorizeEndpointTests()
        {
            Init();
        }

        public void Init()
        {
            var accessor = new HttpContextAccessor();
            accessor.HttpContext = _httpContext;
            _context = new IdentityServerContext(accessor, _options);

            _requestValidationResult.ValidatedRequest = _validatedAuthorizeRequest;

            _subject = new AuthorizeEndpoint(
                _mockEventService, 
                _fakeLogger, 
                _context,
                new StubAuthorizeRequestValidator(_requestValidationResult),
                _stubLocalizationService,
                new FakeHtmlEncoder());
        }

        MockEventService _mockEventService = new MockEventService();
        ILogger<AuthorizeEndpoint> _fakeLogger = new FakeLogger<AuthorizeEndpoint>();
        StubLocalizationService _stubLocalizationService = new StubLocalizationService();

        IdentityServerContext _context;
        IdentityServerOptions _options = new IdentityServerOptions();
        DefaultHttpContext _httpContext = new DefaultHttpContext();
        AuthorizeRequestValidationResult _requestValidationResult = new AuthorizeRequestValidationResult()
        {
            IsError = false,
        };
        ValidatedAuthorizeRequest _validatedAuthorizeRequest = new ValidatedAuthorizeRequest()
        {
            RedirectUri = "http://client/callback",
            State = "123",
            ResponseMode = "fragment",
            ClientId = "client",
            Client = new Client
            {
                ClientId = "client",
                ClientName = "Test Client"
            }
        };

        [Fact]
        [Trait("Category", Category)]
        public async Task post_to_entry_point_returns_405()
        {
            _httpContext.Request.Method = "POST";
            var result = await _subject.ProcessAsync(_httpContext);

            var statusCode = result as StatusCodeResult;
            statusCode.Should().NotBeNull();
            statusCode.StatusCode.Should().Be(405);
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task authorize_request_validation_failure_with_user_error_should_display_error_page_with_error_view_model()
        {
            _requestValidationResult.IsError = true;
            _requestValidationResult.ErrorType = ErrorTypes.User;
            _requestValidationResult.Error = "foo";
            _stubLocalizationService.Result = "foo error message";
            _context.SetRequestId("56789");

            var param = new NameValueCollection();
            var result = await _subject.ProcessRequestAsync(param, null);

            result.Should().BeOfType<ErrorPageResult>();
            var error_result = (ErrorPageResult)result;
            error_result.Model.RequestId.Should().Be("56789");
            error_result.Model.ErrorCode.Should().Be("foo");
            error_result.Model.ErrorMessage.Should().Be("foo error message");
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task authorize_request_validation_failure_with_client_error_should_display_error_page()
        {
            _requestValidationResult.IsError = true;

            var param = new NameValueCollection();
            var result = await _subject.ProcessRequestAsync(param, null);

            result.Should().BeOfType<ErrorPageResult>();
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task authorize_request_validation_failure_error_page_should_contain_return_info()
        {
            _requestValidationResult.IsError = true;
            _requestValidationResult.ErrorType = ErrorTypes.Client;
            _validatedAuthorizeRequest.RedirectUri = "http://client/callback";
            _validatedAuthorizeRequest.State = "123";
            _validatedAuthorizeRequest.ResponseMode = "fragment";
            _validatedAuthorizeRequest.ClientId = "foo_client";
            _validatedAuthorizeRequest.Client = new Client
            {
                ClientId = "foo_client",
                ClientName = "Foo Client"
            };

            var param = new NameValueCollection();
            var result = await _subject.ProcessRequestAsync(param, null);

            var error_result = (ErrorPageResult)result;
            error_result.Model.ReturnInfo.Should().NotBeNull();
            error_result.Model.ReturnInfo.ClientId.Should().Be("foo_client");
            error_result.Model.ReturnInfo.ClientName.Should().Be("Foo Client");
            var parts = error_result.Model.ReturnInfo.Uri.Split('#');
            parts.Length.Should().Be(2);
            parts[0].Should().Be("http://client/callback");
            parts[1].Should().Contain("state=123");
        }

        [Fact]
        [Trait("Category", Category)]
        public async Task authorize_request_validation_failure_raises_failed_endpoint_event()
        {
            _requestValidationResult.IsError = true;
            _requestValidationResult.ErrorType = ErrorTypes.Client;
            _requestValidationResult.Error = "some error";

            var param = new NameValueCollection();
            var result = await _subject.ProcessRequestAsync(param, null);

            var evt = _mockEventService.AssertEventWasRaised<Event<EndpointDetail>>();
            evt.EventType.Should().Be(EventTypes.Failure);
            evt.Id.Should().Be(EventConstants.Ids.EndpointFailure);
            evt.Message.Should().Be("some error");
            evt.Details.EndpointName.Should().Be(EventConstants.EndpointNames.Authorize);
        }
    }
}