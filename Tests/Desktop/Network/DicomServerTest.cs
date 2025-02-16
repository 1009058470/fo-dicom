﻿// Copyright (c) 2012-2021 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Dicom.Helpers;
using Xunit;

namespace Dicom.Network
{
    [Collection("Network"), Trait("Category", "Network"), TestCaseOrderer("Dicom.Helpers.PriorityOrderer", "DICOM [Unit Tests]")]
    public class DicomServerTest
    {
        #region Unit Tests

        [Fact]
        public void Constructor_EstablishTwoWithSamePort_ShouldYieldAccessibleException()
        {
            var port = Ports.GetNext();

            var server1 = DicomServer.Create<DicomCEchoProvider>(port);
            while (!server1.IsListening)
            {
                Thread.Sleep(10);
            }

            var exception = Record.Exception(() => DicomServer.Create<DicomCEchoProvider>(port));
            Assert.IsType<DicomNetworkException>(exception);

            Assert.True(server1.IsListening);
            Assert.Null(server1.Exception);
        }

        [Fact(Skip = "Flaky test. The DICOM Server is not always immediately stopped. We should implement proper cancellation support all the way through DicomService")]
        public void Stop_IsListening_TrueUntilStopRequested()
        {
            var port = Ports.GetNext();

            var server = DicomServer.Create<DicomCEchoProvider>(port);
            while (!server.IsListening)
            {
                Thread.Sleep(10);
            }

            for (var i = 0; i < 10; ++i)
            {
                Thread.Sleep(500);
                Assert.True(server.IsListening);
            }

            server.Stop();
            Thread.Sleep(500);

            Assert.False(server.IsListening);
        }

        [Fact]
        public void Create_GetInstanceSamePort_ReturnsInstance()
        {
            var port = Ports.GetNext();

            using (DicomServer.Create<DicomCEchoProvider>(port))
            {
                var server = DicomServer.GetInstance(port);
                Assert.Equal(port, server.Port);
            }
        }

        [Fact]
        public void Create_GetInstanceSamePortAfterDisposal_ReturnsNull()
        {
            var port = Ports.GetNext();

            using (DicomServer.Create<DicomCEchoProvider>(port)) { /* do nothing here */ }

            var server = DicomServer.GetInstance(port);
            Assert.Null(server);
        }

        [Fact]
        public void Create_TwiceOnSamePortWithDisposalInBetween_DoesNotThrow()
        {
            var port = Ports.GetNext();

            using (DicomServer.Create<DicomCEchoProvider>(port))
            {
                /* do nothing here */
            }

            var e = Record.Exception(
                () =>
                    {
                        using (DicomServer.Create<DicomCEchoProvider>(port))
                        {
                            Assert.NotNull(DicomServer.GetInstance(port));
                        }
                    });
            Assert.Null(e);
        }

        [Fact]
        public void Create_GetInstanceDifferentPort_ReturnsNull()
        {
            var port = Ports.GetNext();

            using (DicomServer.Create<DicomCEchoProvider>(port))
            {
                var server = DicomServer.GetInstance(Ports.GetNext());
                Assert.Null(server);
            }
        }

        [Fact]
        public void Create_MultipleInstancesSamePort_Throws()
        {
            var port = Ports.GetNext();

            using (DicomServer.Create<DicomCEchoProvider>(port))
            {
                var e = Record.Exception(() => DicomServer.Create<DicomCEchoProvider>(port));
                Assert.IsType<DicomNetworkException>(e);
            }
        }

        [Fact]
        public void Create_MultipleInstancesDifferentPorts_AllRegistered()
        {
            var ports = new int[20].Select(i => Ports.GetNext()).ToArray();

            foreach (var port in ports)
            {
                var server = DicomServer.Create<DicomCEchoProvider>(port);
                while (!server.IsListening)
                {
                    Thread.Sleep(10);
                }
            }

            foreach (var port in ports)
            {
                Assert.Equal(port, DicomServer.GetInstance(port).Port);
            }

            foreach (var port in ports)
            {
                DicomServer.GetInstance(port).Dispose();
            }
        }

        [Fact]
        public void IsListening_DicomServerRunningOnPort_ReturnsTrue()
        {
            var port = Ports.GetNext();

            using (var server = DicomServer.Create<DicomCEchoProvider>(port))
            {
                while (!server.IsListening)
                {
                    Thread.Sleep(10);
                }

                Assert.True(DicomServer.IsListening(port));
            }
        }

        [Fact]
        public void IsListening_DicomServerStoppedOnPort_ReturnsFalse()
        {
            var port = Ports.GetNext();

            using (var server = DicomServer.Create<DicomCEchoProvider>(port))
            {
                server.Stop();
                Thread.Sleep(500);

                Assert.NotNull(DicomServer.GetInstance(port));
                Assert.False(DicomServer.IsListening(port));
            }
        }

        [Fact]
        public void IsListening_DicomServerNotInitializedOnPort_ReturnsFalse()
        {
            var port = Ports.GetNext();

            using (var server = DicomServer.Create<DicomCEchoProvider>(port))
            {
                Assert.False(DicomServer.IsListening(Ports.GetNext()));
            }
        }

        [Fact]
        public async Task SendMaxPDU()
        {
            var port = Ports.GetNext();
            uint serverPduLength = 400000;
            uint clientPduLength = serverPduLength / 2;
            using(var server = DicomServer.Create<DicomCEchoProvider>(port, null, new DicomServiceOptions {  MaxPDULength = serverPduLength }))
            {
                uint serverPduInAssociationAccepted = 0;
                var client = new Client.DicomClient("127.0.0.1", port, false, "SCU", "ANY-SCP");
                client.Options = new DicomServiceOptions { MaxPDULength = clientPduLength }; // explicitly choose a different value
                await client.AddRequestAsync(new DicomCEchoRequest());
                client.AssociationAccepted += (sender, e) => serverPduInAssociationAccepted = e.Association.MaximumPDULength;

                await client.SendAsync();

                Assert.Equal(serverPduLength, serverPduInAssociationAccepted);
            }
        }

        [Fact]
        public void Send_KnownSOPClass_SendSucceeds()
        {
            var port = Ports.GetNext();
            using (DicomServer.Create<SimpleCStoreProvider>(port))
            {
                DicomStatus status = null;
                var request = new DicomCStoreRequest(@".\Test Data\CT-MONO2-16-ankle")
                {
                    OnResponseReceived = (req, res) => status = res.Status
                };

                var client = new DicomClient();
                client.AddRequest(request);

                client.Send("127.0.0.1", port, false, "SCU", "ANY-SCP");

                Assert.Equal(DicomStatus.Success, status);
            }
        }

        [Fact, TestPriority(1)]
        public void Send_PrivateNotRegisteredSOPClass_SendFails()
        {
            // use a different UID that the one in test Send_PrivateRegisteredSOPClass_SendSucceeds, which will be registered in DicomUID class.
            var uid = new DicomUID("1.1.1.2", "Private Fo-Dicom Storage", DicomUidType.SOPClass);
            var ds = new DicomDataset(
               new DicomUniqueIdentifier(DicomTag.SOPClassUID, uid),
               new DicomUniqueIdentifier(DicomTag.SOPInstanceUID, "1.2.3.4.5"));
            var port = Ports.GetNext();
            using (DicomServer.Create<SimpleCStoreProvider>(port))
            {
                DicomStatus status = null;
                var request = new DicomCStoreRequest(new DicomFile(ds))
                {
                    OnResponseReceived = (req, res) => status = res.Status
                };

                var client = new DicomClient();
                client.AddRequest(request);

                client.Send("127.0.0.1", port, false, "SCU", "ANY-SCP");

                Assert.Equal(DicomStatus.SOPClassNotSupported, status);
            }
        }

        [Fact, TestPriority(2)]
        public void Send_PrivateRegisteredSOPClass_SendSucceeds()
        {
            var uid = new DicomUID("1.1.1.1", "Private Fo-Dicom Storage", DicomUidType.SOPClass);
            DicomUID.Register(uid);
            var ds = new DicomDataset(
                new DicomUniqueIdentifier(DicomTag.SOPClassUID, uid),
                new DicomUniqueIdentifier(DicomTag.SOPInstanceUID, "1.2.3.4.5"));

            var port = Ports.GetNext();
            using (DicomServer.Create<SimpleCStoreProvider>(port))
            {
                DicomStatus status = null;
                var request = new DicomCStoreRequest(new DicomFile(ds))
                {
                    OnResponseReceived = (req, res) => status = res.Status
                };

                var client = new DicomClient();
                client.AddRequest(request);

                client.Send("127.0.0.1", port, false, "SCU", "ANY-SCP");

                Assert.Equal(DicomStatus.Success, status);
            }
        }

        [Fact]
        public void Stop_DisconnectedClientsCount_ShouldBeZeroAfterShortDelay()
        {
            var port = Ports.GetNext();

            using (var server = DicomServer.Create<DicomCEchoProvider>(port))
            {
                while (!server.IsListening)
                {
                    Thread.Sleep(10);
                }

                var client = new DicomClient();
                client.AddRequest(new DicomCEchoRequest());
                client.Send("127.0.0.1", port, false, "SCU", "ANY-SCP");
                client.Release(0);
                Thread.Sleep(100);

                server.Stop();
                Thread.Sleep(100);

                var actual = ((DicomServer<DicomCEchoProvider>) server).CompletedServicesCount;
                Assert.Equal(0, actual);
            }
        }

        [Fact]
        public void Send_LoopbackListenerKnownSOPClass_SendSucceeds()
        {
            var port = Ports.GetNext();
            using (DicomServer.Create<SimpleCStoreProvider>(NetworkManager.IPv4Loopback, port))
            {
                DicomStatus status = null;
                var request = new DicomCStoreRequest(@".\Test Data\CT-MONO2-16-ankle")
                {
                    OnResponseReceived = (req, res) => status = res.Status
                };

                var client = new DicomClient();
                client.AddRequest(request);

                client.Send(NetworkManager.IPv4Loopback, port, false, "SCU", "ANY-SCP");

                Assert.Equal(DicomStatus.Success, status);
            }
        }

        [Fact]
        public void Send_Ipv6AnyListenerKnownSOPClass_SendSucceeds()
        {
            var port = Ports.GetNext();
            using (DicomServer.Create<SimpleCStoreProvider>(NetworkManager.IPv6Any, port))
            {
                DicomStatus status = null;
                var request = new DicomCStoreRequest(@".\Test Data\CT-MONO2-16-ankle")
                {
                    OnResponseReceived = (req, res) => status = res.Status
                };

                var client = new DicomClient();
                client.AddRequest(request);

                client.Send(NetworkManager.IPv6Loopback, port, false, "SCU", "ANY-SCP");

                Assert.Equal(DicomStatus.Success, status);
            }
        }

        [Fact]
        public void Send_FromIpv4ToIpv6AnyListenerKnownSOPClass_SendFails()
        {
            var port = Ports.GetNext();
            using (DicomServer.Create<SimpleCStoreProvider>(NetworkManager.IPv6Any, port))
            {
                var request = new DicomCStoreRequest(@".\Test Data\CT-MONO2-16-ankle");

                var client = new DicomClient();
                client.AddRequest(request);

                var exception = Record.Exception(() => client.Send(NetworkManager.IPv4Loopback, port, false, "SCU", "ANY-SCP"));

                Assert.IsAssignableFrom<SocketException>(exception);
            }
        }

        [Fact]
        public void Send_FromIpv6ToIpv4AnyListenerKnownSOPClass_SendFails()
        {
            var port = Ports.GetNext();
            using (DicomServer.Create<SimpleCStoreProvider>(NetworkManager.IPv4Any, port))
            {
                var request = new DicomCStoreRequest(@".\Test Data\CT-MONO2-16-ankle");

                var client = new DicomClient();
                client.AddRequest(request);

                var exception = Record.Exception(() => client.Send(NetworkManager.IPv6Loopback, port, false, "SCU", "ANY-SCP"));

                Assert.IsAssignableFrom<SocketException>(exception);
            }
        }

        [Fact]
        public void CanCreateIpv4AndIpv6()
        {
            var port = Ports.GetNext();
            using (DicomServer.Create<DicomCEchoProvider>(port))
            {
                var e = Record.Exception(
                    () =>
                    {
                        using (DicomServer.Create<DicomCEchoProvider>(NetworkManager.IPv6Any, port))
                        {
                            /* do nothing here */
                        }
                    });
                Assert.Null(e);
            }
        }

        [Fact]
        public void Create_SubclassedServer_SufficientlyCreated()
        {
            var port = Ports.GetNext();

            using (var server = DicomServer.Create<DicomCEchoProvider, DicomCEchoProviderServer>(null, port))
            {
                Assert.IsType<DicomCEchoProviderServer>(server);
                Assert.Equal(DicomServer.GetInstance(port), server);

                var status = DicomStatus.UnrecognizedOperation;
                var handle = new ManualResetEventSlim();

                var client = new DicomClient();
                client.AddRequest(new DicomCEchoRequest
                {
                    OnResponseReceived = (req, rsp) =>
                    {
                        status = rsp.Status;
                        handle.Set();
                    }
                });
                client.Send("127.0.0.1", port, false, "SCU", "ANY-SCP");

                handle.Wait(1000);
                Assert.Equal(DicomStatus.Success, status);
            }
        }

        private void TestFoDicomUnhandledException(int port)
        {
            var server = DicomServer.Create<DicomCEchoProvider>(port);
            Thread.Sleep(500);
            server.Stop();
        }

        [Fact]
        public void StopServerWithoutException()
        {
            object ue = null;
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => ue = args.ExceptionObject;
            TaskScheduler.UnobservedTaskException += (sender, args) => ue = args.Exception;

            var port = Ports.GetNext();

            Task.Factory.StartNew(() => TestFoDicomUnhandledException(port));

            Thread.Sleep(2000);
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.Null(ue);
        }

        #endregion
    }


    #region Support Types

    public class DicomCEchoProviderServer : DicomServer<DicomCEchoProvider>
    {
        protected override DicomCEchoProvider CreateScp(INetworkStream stream) => new DicomCEchoProvider(stream, null, null);
    }

    #endregion

}

