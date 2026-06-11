// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenClaw.Services;

namespace OpenClaw.Core.Tests;

[TestClass]
public sealed class GatewayHttpStatusClassifierTests
{
    [TestMethod]
    public void Status530BehindCloudflareIsTunnelUnavailable()
    {
        var classification = GatewayHttpStatusClassifier.Classify((HttpStatusCode)530, "Origin Unreachable", viaCloudflare: true);

        Assert.AreEqual(GatewayHttpStatusKind.CloudflareTunnelUnavailable, classification.Kind);
        Assert.IsFalse(classification.IsReachable);
        Assert.AreEqual(530, classification.StatusCode);
    }

    [TestMethod]
    public void Status530WithoutCloudflareIsServerOrProxyError()
    {
        var classification = GatewayHttpStatusClassifier.Classify((HttpStatusCode)530, null, viaCloudflare: false);

        Assert.AreEqual(GatewayHttpStatusKind.ServerOrProxyError, classification.Kind);
        Assert.IsFalse(classification.IsReachable);
    }

    [TestMethod]
    public void Status200IsReachable()
    {
        var classification = GatewayHttpStatusClassifier.Classify(HttpStatusCode.OK, "OK", viaCloudflare: false);

        Assert.AreEqual(GatewayHttpStatusKind.Reachable, classification.Kind);
        Assert.IsTrue(classification.IsReachable);
    }

    [TestMethod]
    public void Status401And403AreAccessRequiredButReachable()
    {
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
        {
            var classification = GatewayHttpStatusClassifier.Classify(status, null, viaCloudflare: false);

            Assert.AreEqual(GatewayHttpStatusKind.AccessRequired, classification.Kind, $"status={status}");
            Assert.IsTrue(classification.IsReachable, $"status={status}");
        }
    }

    [TestMethod]
    public void Status409IsWaitingApproval()
    {
        var classification = GatewayHttpStatusClassifier.Classify(HttpStatusCode.Conflict, "Conflict", viaCloudflare: true);

        Assert.AreEqual(GatewayHttpStatusKind.GatewayWaitingApproval, classification.Kind);
        Assert.IsTrue(classification.IsReachable);
    }

    [TestMethod]
    public void Status429IsAuthRateLimited()
    {
        var classification = GatewayHttpStatusClassifier.Classify(HttpStatusCode.TooManyRequests, null, viaCloudflare: false);

        Assert.AreEqual(GatewayHttpStatusKind.AuthRateLimited, classification.Kind);
        Assert.IsTrue(classification.IsReachable);
    }

    [TestMethod]
    public void Status404IsMissingPathAndNotReachable()
    {
        var classification = GatewayHttpStatusClassifier.Classify(HttpStatusCode.NotFound, null, viaCloudflare: false);

        Assert.AreEqual(GatewayHttpStatusKind.MissingPath, classification.Kind);
        Assert.IsFalse(classification.IsReachable);
    }

    [TestMethod]
    public void Status500IsServerOrProxyError()
    {
        var classification = GatewayHttpStatusClassifier.Classify(HttpStatusCode.InternalServerError, null, viaCloudflare: false);

        Assert.AreEqual(GatewayHttpStatusKind.ServerOrProxyError, classification.Kind);
        Assert.IsFalse(classification.IsReachable);
    }
}
