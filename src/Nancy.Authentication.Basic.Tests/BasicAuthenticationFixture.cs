﻿using System;
using System.Collections.Generic;
using System.Text;
using FakeItEasy;
using Nancy.Bootstrapper;
using Nancy.Security;
using Nancy.Tests;
using Nancy.Tests.Fakes;
using Xunit;

namespace Nancy.Authentication.Basic.Tests
{
    public class BasicAuthenticationFixture
    {
        readonly BasicAuthenticationConfiguration config;
        readonly IApplicationPipelines hooks;

        public BasicAuthenticationFixture()
        {
            config = new BasicAuthenticationConfiguration(A.Fake<IUserValidator>(), "realm");
            hooks = new FakeApplicationPipelines();
            BasicAuthentication.Enable(hooks, config);
        }

        [Fact]
        public void Should_add_a_pre_and_post_hook_in_application_when_enabled()
        {
            // Given
            var pipelines = A.Fake<IApplicationPipelines>();

            // When
            BasicAuthentication.Enable(pipelines, config);

            // Then
            A.CallTo(() => pipelines.BeforeRequest.AddItemToStartOfPipeline(A<Func<NancyContext, Response>>.Ignored))
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public void Should_add_both_basic_and_requires_auth_pre_and_post_hooks_in_module_when_enabled()
        {
            // Given
            var module = new FakeModule();

            // When
            BasicAuthentication.Enable(module, config);

            // Then
            module.Before.PipelineItems.ShouldHaveCount(2);
        }

        [Fact]
        public void Should_throw_with_null_config_passed_to_enable_with_application()
        {
            // Given, When
            Exception result = Record.Exception(() => BasicAuthentication.Enable(A.Fake<IApplicationPipelines>(), null));

            // Then
            result.ShouldBeOfType(typeof (ArgumentNullException));
        }

        [Fact]
        public void Should_throw_with_null_config_passed_to_enable_with_module()
        {
            // Given, When
            Exception result = Record.Exception(() => BasicAuthentication.Enable(new FakeModule(), null));

            // Then
            result.ShouldBeOfType(typeof (ArgumentNullException));
        }

        [Fact]
        public void Pre_request_hook_should_not_set_auth_details_with_no_auth_headers()
        {
            // Given
            var context = new NancyContext
            {
                Request = new FakeRequest("GET", "/")
            };

            // When
            Response result = hooks.BeforeRequest.Invoke(context);

            // Then
            result.ShouldBeNull();
            context.Items.ContainsKey(SecurityConventions.AuthenticatedUsernameKey).ShouldBeFalse();
        }

        [Fact]
        public void Post_request_hook_should_return_challenge_when_unauthorized_returned_from_route()
        {
            // Given
            var context = new NancyContext
            {
                Request = new FakeRequest("GET", "/")
            };

            string wwwAuthenticate;
            context.Response = new Response {StatusCode = HttpStatusCode.Unauthorized};

            // When
            hooks.AfterRequest.Invoke(context);

            // Then
            context.Response.Headers.TryGetValue("WWW-Authenticate", out wwwAuthenticate);
            context.Response.StatusCode.ShouldEqual(HttpStatusCode.Unauthorized);
            context.Response.Headers.ContainsKey("WWW-Authenticate").ShouldBeTrue();
            context.Response.Headers["WWW-Authenticate"].ShouldContain("Basic");
            context.Response.Headers["WWW-Authenticate"].ShouldContain("realm=\"" + config.Realm + "\"");
        }

        [Fact]
        public void Pre_request_hook_should_not_set_auth_details_when_invalid_scheme_in_auth_header()
        {
            // Given
            NancyContext context = CreateContextWithHeader(
                "Authorization", new[] {"FooScheme" + " " + EncodeCredentials("foo", "bar")});

            // When
            Response result = hooks.BeforeRequest.Invoke(context);

            // Then
            result.ShouldBeNull();
            context.Items.ContainsKey(SecurityConventions.AuthenticatedUsernameKey).ShouldBeFalse();
        }

        [Fact]
        public void Pre_request_hook_should_not_authenticate_when_invalid_encoded_username_in_auth_header()
        {
            // Given
            NancyContext context = CreateContextWithHeader(
                "Authorization", new[] {"Basic" + " " + "some credentials"});

            // When
            Response result = hooks.BeforeRequest.Invoke(context);

            // Then
            result.ShouldBeNull();
            context.Items.ContainsKey(SecurityConventions.AuthenticatedUsernameKey).ShouldBeFalse();
        }

        [Fact]
        public void Pre_request_hook_should_call_user_validator_with_username_in_auth_header()
        {
            // Given
            NancyContext context = CreateContextWithHeader(
                "Authorization", new[] {"Basic" + " " + EncodeCredentials("foo", "bar")});

            // When
            hooks.BeforeRequest.Invoke(context);

            // Then
            A.CallTo(() => config.UserValidator.Validate("foo", "bar")).MustHaveHappened();
        }

        [Fact]
        public void Should_set_username_in_context_with_valid_username_in_auth_header()
        {
            // Given
            var fakePipelines = new FakeApplicationPipelines();

            var validator = A.Fake<IUserValidator>();
            A.CallTo(() => validator.Validate("foo", "bar")).Returns(true);

            var cfg = new BasicAuthenticationConfiguration(validator, "realm");

            NancyContext context = CreateContextWithHeader(
                "Authorization", new[] {"Basic" + " " + EncodeCredentials("foo", "bar")});

            BasicAuthentication.Enable(fakePipelines, cfg);

            // When
            fakePipelines.BeforeRequest.Invoke(context);

            // Then
            context.Items[SecurityConventions.AuthenticatedUsernameKey].ShouldEqual("foo");
        }

        static NancyContext CreateContextWithHeader(string name, IEnumerable<string> values)
        {
            var header = new Dictionary<string, IEnumerable<string>>
            {
                {name, values}
            };

            return new NancyContext
            {
                Request = new FakeRequest("GET", "/", header)
            };
        }

        static string EncodeCredentials(string username, string password)
        {
            string credentials = string.Format("{0}:{1}", username, password);

            string encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

            return encodedCredentials;
        }

        class FakeModule : NancyModule {}

        public class FakeApplicationPipelines : IApplicationPipelines
        {
            public FakeApplicationPipelines()
            {
                BeforeRequest = new BeforePipeline();
                AfterRequest = new AfterPipeline();
            }

            public BeforePipeline BeforeRequest { get; set; }

            public AfterPipeline AfterRequest { get; set; }

            public ErrorPipeline OnError { get; set; }
        }
    }
}
