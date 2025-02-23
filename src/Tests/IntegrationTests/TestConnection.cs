﻿// Copyright 2015-2018 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using NATS.Client;
using System.Threading;
using Xunit;
using System.Diagnostics;

namespace IntegrationTests
{
    /// <summary>
    /// Run these tests with the gnatsd auth.conf configuration file.
    /// </summary>
    [Collection(TestCollections.Default)]
    public class TestConnection
    {
        UnitTestUtilities utils = new UnitTestUtilities();
        
        [Fact]
        public void TestConnectionStatus()
        {
            using (new NATSServer())
            {
                IConnection c = utils.DefaultTestConnection;
                Assert.Equal(ConnState.CONNECTED, c.State);
                c.Close();
                Assert.Equal(ConnState.CLOSED, c.State);
            }
        }

        [Fact]
        public void TestCloseHandler()
        {
            AutoResetEvent ev = new AutoResetEvent(false);
            using (new NATSServer())
            {
                Options o = utils.DefaultTestOptions;
                o.ClosedEventHandler += (sender, args) =>
                {
                    ev.Set();
                };
                IConnection c = new ConnectionFactory().CreateConnection(o);
                c.Close();
                Assert.True(ev.WaitOne(1000));

                // now test using.
                ev.Reset();
                using (c = new ConnectionFactory().CreateConnection(o)) { };
                Assert.True(ev.WaitOne(1000));
            }
        }

        [Fact]
        public void TestCloseDisconnectedHandler()
        {
            using (new NATSServer())
            {
                bool disconnected = false;
                Object mu = new Object();

                Options o = utils.DefaultTestOptions;
                o.AllowReconnect = false;
                o.DisconnectedEventHandler += (sender, args) =>
                {
                    lock (mu)
                    {
                        disconnected = true;
                        Monitor.Pulse(mu);
                    }
                };

                IConnection c = new ConnectionFactory().CreateConnection(o);
                lock (mu)
                {
                    c.Close();
                    Monitor.Wait(mu, 20000);
                }
                Assert.True(disconnected);

                // now test using.
                disconnected = false;
                lock (mu)
                {
                    using (c = new ConnectionFactory().CreateConnection(o)) { };
                    Monitor.Wait(mu, 20000);
                }
                Assert.True(disconnected);
            }
        }

        [Fact]
        public void TestServerStopDisconnectedHandler()
        {
            using (var s = new NATSServer())
            {
                AutoResetEvent ev = new AutoResetEvent(false);

                Options o = utils.DefaultTestOptions;
                o.AllowReconnect = false;
                o.DisconnectedEventHandler += (sender, args) =>
                {
                    ev.Set();
                };

                IConnection c = new ConnectionFactory().CreateConnection(o);
                s.Bounce(1000);

                Assert.True(ev.WaitOne(10000));

                c.Close();
            }
        }

        [Fact]
        public void TestClosedConnections()
        {
            using (new NATSServer())
            {
                IConnection c = utils.DefaultTestConnection;
                ISyncSubscription s = c.SubscribeSync("foo");

                c.Close();

                // While we can annotate all the exceptions in the test framework,
                // just do it manually.
                Assert.ThrowsAny<NATSConnectionClosedException>(() => c.Publish("foo", null));

                Assert.ThrowsAny<NATSConnectionClosedException>(() => c.Publish(new Msg("foo")));

                Assert.ThrowsAny<NATSConnectionClosedException>(() => c.SubscribeAsync("foo"));

                Assert.ThrowsAny<NATSConnectionClosedException>(() => c.SubscribeSync("foo"));

                Assert.ThrowsAny<NATSConnectionClosedException>(() => c.SubscribeAsync("foo", "bar"));

                Assert.ThrowsAny<NATSConnectionClosedException>(() => c.SubscribeSync("foo", "bar"));

                Assert.ThrowsAny<NATSConnectionClosedException>(() => c.Request("foo", null));

                Assert.ThrowsAny<NATSConnectionClosedException>(() => s.NextMessage());

                Assert.ThrowsAny<NATSConnectionClosedException>(() => s.NextMessage(100));

                Assert.ThrowsAny<NATSConnectionClosedException>(() => s.Unsubscribe());

                Assert.ThrowsAny<NATSConnectionClosedException>(() => s.AutoUnsubscribe(1));
            }
        }

        [Fact]
        public void TestConnectVerbose()
        {
            using (new NATSServer())
            {
                Options o = utils.DefaultTestOptions;
                o.Verbose = true;

                IConnection c = new ConnectionFactory().CreateConnection(o);
                c.Close();
            }
        }

        [Fact]
        public void TestServerDiscoveredHandlerNotCalledOnConnect()
        {
            using (new NATSServer())
            {
                Options o = utils.DefaultTestOptions;

                bool serverDiscoveredCalled = false;

                o.ServerDiscoveredEventHandler += (sender, e) =>
                {
                    serverDiscoveredCalled = true;
                };

                IConnection c = new ConnectionFactory().CreateConnection(o);
                c.Close();

                Assert.False(serverDiscoveredCalled);
            }
        }


        [Fact]
        public void TestNKey()
        {
            using (utils.CreateServerWithConfig("nkey.conf"))
            {
                Options opts = ConnectionFactory.GetDefaultOptions();

                // See nkey.conf
                opts.SetNkey("UCKKTOZV72L3NITTGNOCRDZUI5H632XCT4ZWPJBC2X3VEY72KJUWEZ2Z", "./config/certs/user.nk");
                new ConnectionFactory().CreateConnection(opts).Close();
            }
        }

        [Fact]
        public void TestInvalidNKey()
        {
            using (utils.CreateServerWithConfig("nkey.conf"))
            {
                Options opts = ConnectionFactory.GetDefaultOptions();

                opts.SetNkey("XXKKTOZV72L3NITTGNOCRDZUI5H632XCT4ZWPJBC2X3VEY72KJUWEZ2Z", "./config/certs/user.nk");
                Assert.Throws<NATSConnectionException>(()=> new ConnectionFactory().CreateConnection(opts).Close());

                
                Assert.Throws<ArgumentException>(() => opts.SetNkey("", "./config/certs/user.nk"));
                Assert.Throws<ArgumentException>(() => opts.SetNkey("UCKKTOZV72L3NITTGNOCRDZUI5H632XCT4ZWPJBC2X3VEY72KJUWEZ2Z", ""));
            }
        }

        [Fact]
        public void Test20Security()
        {
            IConnection c = null;
            AutoResetEvent ev = new AutoResetEvent(false);
            using (utils.CreateServerWithConfig("operator.conf"))
            {
                Options opts = ConnectionFactory.GetDefaultOptions();
                opts.ReconnectedEventHandler += (obj, args) => {
                    ev.Set();
                };
                opts.SetUserCredentials("./config/certs/test.creds");
                c = new ConnectionFactory().CreateConnection(opts);
            }

            // effectively bounce the server
            using (utils.CreateServerWithConfig("operator.conf"))
            {
                // wait for reconnect.
                Assert.True(ev.WaitOne(60000));
            }
        }

        [Fact]
        public void Test20SecurityFactoryAPI()
        {
            using (utils.CreateServerWithConfig("operator.conf"))
            {
                new ConnectionFactory().CreateConnection("nats://127.0.0.1",
                    "./config/certs/test.creds").Close();
                new ConnectionFactory().CreateConnection("nats://127.0.0.1",
                    "./config/certs/test.creds", "./config/certs/test.creds").Close();

                Assert.Throws<ArgumentException>(() => new ConnectionFactory().CreateConnection("nats://127.0.0.1", ""));
                Assert.Throws<ArgumentException>(() => new ConnectionFactory().CreateConnection("nats://127.0.0.1", null));
                Assert.Throws<ArgumentException>(() => new ConnectionFactory().CreateConnection("nats://127.0.0.1", "my.creds", ""));
                Assert.Throws<ArgumentException>(() => new ConnectionFactory().CreateConnection("nats://127.0.0.1", "my.creds", null));
            }
        }

        [Fact]
        public void Test20SecurityHandlerExceptions()
        {
            bool userThrown = false;
            bool sigThrown = false;
            using (utils.CreateServerWithConfig("operator.conf"))
            {
                EventHandler<UserJWTEventArgs> jwtEh = (sender, args) =>
                {
                    if (!userThrown)
                    {
                        userThrown = true;
                        throw new Exception("Exception from the user JWT handler.");
                    }
                    args.JWT = "somejwt";
                };

                EventHandler<UserSignatureEventArgs> sigEh = (sender, args) =>
                {
                    sigThrown = true;
                    throw new Exception("Exception from the sig handler.");     
                };
                Options opts = ConnectionFactory.GetDefaultOptions();
                opts.SetUserCredentialHandlers(jwtEh, sigEh);

                Assert.Throws<NATSConnectionException>(() => new ConnectionFactory().CreateConnection(opts));
                Assert.Throws<NATSConnectionException>(() => new ConnectionFactory().CreateConnection(opts));
                Assert.True(userThrown);
                Assert.True(sigThrown);
            }
        }

        [Fact]
        public void Test20SecurityHandlerNoJWTSet()
        {
            using (utils.CreateServerWithConfig("operator.conf"))
            {
                var opts = ConnectionFactory.GetDefaultOptions();
                opts.SetUserCredentialHandlers((sender, args) =>{}, (sender, args) => { });
                Assert.Throws<NATSConnectionException>(() => new ConnectionFactory().CreateConnection(opts));
            }
        }

        [Fact]
        public void Test20SecurityHandlerNoSigSet()
        {
            using (utils.CreateServerWithConfig("operator.conf"))
            {
                var opts = ConnectionFactory.GetDefaultOptions();
                opts.SetUserCredentialHandlers((sender, args) => { args.JWT = "somejwt"; }, (sender, args) => { });
                Assert.Throws<NATSConnectionException>(() => new ConnectionFactory().CreateConnection(opts));
            }
        }

        [Fact]
        public void Test20SecurityHandlers()
        {
            string userJWT = "eyJ0eXAiOiJqd3QiLCJhbGciOiJlZDI1NTE5In0.e" +
                "yJqdGkiOiJFU1VQS1NSNFhGR0pLN0FHUk5ZRjc0STVQNTZHMkFGWER" + 
                "YQ01CUUdHSklKUEVNUVhMSDJBIiwiaWF0IjoxNTQ0MjE3NzU3LCJpc" +
                "3MiOiJBQ1pTV0JKNFNZSUxLN1FWREVMTzY0VlgzRUZXQjZDWENQTUV" + 
                "CVUtBMzZNSkpRUlBYR0VFUTJXSiIsInN1YiI6IlVBSDQyVUc2UFY1N" +
                "TJQNVNXTFdUQlAzSDNTNUJIQVZDTzJJRUtFWFVBTkpYUjc1SjYzUlE" +
                "1V002IiwidHlwZSI6InVzZXIiLCJuYXRzIjp7InB1YiI6e30sInN1Y" +
                "iI6e319fQ.kCR9Erm9zzux4G6M-V2bp7wKMKgnSNqMBACX05nwePRW" +
                "Qa37aO_yObbhcJWFGYjo1Ix-oepOkoyVLxOJeuD8Bw";

            string userSeed = "SUAIBDPBAUTWCWBKIO6XHQNINK5FWJW4OHLXC3HQ" +
                "2KFE4PEJUA44CNHTC4";

            using (utils.CreateServerWithConfig("operator.conf"))
            {
                EventHandler<UserJWTEventArgs> jwtEh = (sender, args) =>
                {
                    //just return a jwt
                    args.JWT = userJWT;
                };

                EventHandler<UserSignatureEventArgs> sigEh = (sender, args) =>
                {
                    // generate a nats key pair from a private key.
                    // NEVER EVER handle a real private key/seed like this.
                    var kp = Nkeys.FromSeed(userSeed);
                    args.SignedNonce = kp.Sign(args.ServerNonce);
                };
                Options opts = ConnectionFactory.GetDefaultOptions();
                opts.SetUserCredentialHandlers(jwtEh, sigEh);
                new ConnectionFactory().CreateConnection(opts).Close();
            }
        }

        [Fact]
        public void TestJWTFunctionality()
        {
            using (new NATSServer())
            {

            }
        }


        [Fact(Skip = "WorkInProgress")]
        // This test works locally, but fails in AppVeyor some of the time
        // TODO:  Work to identify why this happens...
        private void TestCallbacksOrder()
        {
            bool firstDisconnect = true;

            long orig = DateTime.Now.Ticks;

            long dtime1 = orig;
            long dtime2 = orig;
            long rtime = orig;
            long atime1 = orig;
            long atime2 = orig;
            long ctime = orig;

            AutoResetEvent reconnected = new AutoResetEvent(false);
            AutoResetEvent closed      = new AutoResetEvent(false);
            AutoResetEvent asyncErr1   = new AutoResetEvent(false);
            AutoResetEvent asyncErr2   = new AutoResetEvent(false);
            AutoResetEvent recvCh      = new AutoResetEvent(false);
            AutoResetEvent recvCh1     = new AutoResetEvent(false);
            AutoResetEvent recvCh2     = new AutoResetEvent(false);

            using (NATSServer s = utils.CreateServerWithConfig("auth_1222.conf"),
                   def = new NATSServer())
            {
                Options o = utils.DefaultTestOptions;

                o.DisconnectedEventHandler += (sender, args) =>
                {
                    Thread.Sleep(100);
                    if (firstDisconnect)
                    {
                        firstDisconnect = false;
                        dtime1 = DateTime.Now.Ticks;
                    }
                    else
                    {
                        dtime2 = DateTime.Now.Ticks;
                    }
                };

                o.ReconnectedEventHandler += (sender, args) =>
                {
                    Thread.Sleep(100);
                    rtime = DateTime.Now.Ticks;
                    reconnected.Set();
                };

                o.AsyncErrorEventHandler += (sender, args) =>
                {
                    Thread.Sleep(100);
                    if (args.Subscription.Subject.Equals("foo"))
                    {
                        atime1 = DateTime.Now.Ticks;
                        asyncErr1.Set();
                    }
                    else
                    {
                        atime2 = DateTime.Now.Ticks;
                        asyncErr2.Set();
                    }
                };

                o.ClosedEventHandler += (sender, args) =>
                {
                    ctime = DateTime.Now.Ticks;
                    closed.Set();
                };

                o.ReconnectWait = 500;
                o.NoRandomize = true;
                o.Servers = new string[] { "nats://localhost:4222", "nats://localhost:1222" };
                o.SubChannelLength = 1;

                using (IConnection nc = new ConnectionFactory().CreateConnection(o),
                       ncp = utils.DefaultTestConnection)
                {
                    // On hosted environments, some threads/tasks can start before others
                    // due to resource constraints.  Allow time to start.
                    Thread.Sleep(1000);

                    def.Bounce(1000);

                    Thread.Sleep(1000);

                    Assert.True(reconnected.WaitOne(3000));

                    object asyncLock = new object();
                    EventHandler<MsgHandlerEventArgs> eh = (sender, args) =>
                    {
                        lock (asyncLock)
                        {
                            recvCh.Set();
                            if (args.Message.Subject.Equals("foo"))
                            {
                                recvCh1.Set();
                            }
                            else
                            {
                                recvCh2.Set();
                            }
                        }
                    };

                    IAsyncSubscription sub1 = nc.SubscribeAsync("foo", eh);
                    IAsyncSubscription sub2 = nc.SubscribeAsync("bar", eh);
                    nc.Flush();

                    ncp.Publish("foo", System.Text.Encoding.UTF8.GetBytes("hello"));
                    ncp.Publish("bar", System.Text.Encoding.UTF8.GetBytes("hello"));
                    ncp.Flush();

                    recvCh.WaitOne(3000);

                    for (int i = 0; i < 3; i++)
                    {
                        ncp.Publish("foo", System.Text.Encoding.UTF8.GetBytes("hello"));
                        ncp.Publish("bar", System.Text.Encoding.UTF8.GetBytes("hello"));
                    }

                    ncp.Flush();

                    Assert.True(asyncErr1.WaitOne(3000));
                    Assert.True(asyncErr2.WaitOne(3000));

                    def.Shutdown();

                    Thread.Sleep(1000);
                    closed.Reset();
                    nc.Close();

                    Assert.True(closed.WaitOne(3000));
                }


                if (dtime1 == orig || dtime2 == orig || rtime == orig ||
                    atime1 == orig || atime2 == orig || ctime == orig)
                {
                    Console.WriteLine("Error = callback didn't fire: {0}\n{1}\n{2}\n{3}\n{4}\n{5}\n",
                        dtime1, dtime2, rtime, atime1, atime2, ctime);
                    throw new Exception("Callback didn't fire.");
                }

                if (rtime < dtime1 || dtime2 < rtime || ctime < atime2)
                {
                    Console.WriteLine("Wrong callback order:\n" +
                        "dtime1: {0}\n" +
                        "rtime:  {1}\n" +
                        "atime1: {2}\n" +
                        "atime2: {3}\n" +
                        "dtime2: {4}\n" +
                        "ctime:  {5}\n",
                        dtime1, rtime, atime1, atime2, dtime2, ctime);
                    throw new Exception("Invalid callback order.");
                }
            }
        }

        [Fact]
        public void TestConnectionMemoryLeak()
        {
            using (new NATSServer())
            {
                var sw = new Stopwatch();

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                sw.Start();

                ConnectionFactory cf = new ConnectionFactory();

                long memStart = Process.GetCurrentProcess().PrivateMemorySize64;

                int count = 0;
                while (sw.ElapsedMilliseconds < 10000 || count < 2000)
                {
                    count++;
                    using (IConnection conn = cf.CreateConnection()) { }
                }

                cf = null;

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

                double memGrowthPercent = 100 * (
                    ((double)(Process.GetCurrentProcess().PrivateMemorySize64 - memStart))
                        / (double)memStart);

                Assert.True(memGrowthPercent < 30.0);
            }
        }

        [Fact]
        public void TestConnectionSubscriberMemoryLeak()
        {
            using (new NATSServer())
            {
                var sw = new Stopwatch();

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                sw.Start();

                ConnectionFactory cf = new ConnectionFactory();

                long memStart = Process.GetCurrentProcess().PrivateMemorySize64;

                while (sw.ElapsedMilliseconds < 10000)
                {
                    using (IConnection conn = cf.CreateConnection()) {
                        conn.SubscribeAsync("foo", (obj, args) =>
                        {
                            // NOOP
                        });

                        var sub = conn.SubscribeAsync("foo");
                        sub.MessageHandler += (obj, args) =>
                        {
                            // NOOP
                        };
                        sub.Start();

                        conn.SubscribeSync("foo");
                    }
                }

                cf = null;

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

                double memGrowthPercent = 100 * (
                    ((double)(Process.GetCurrentProcess().PrivateMemorySize64 - memStart))
                        / (double)memStart);

                Assert.True(memGrowthPercent < 30.0);
            }
        }

        [Fact]
        public void TestConnectionCloseAndDispose()
        {
            using (new NATSServer())
            {
                // test that dispose code works after a connection
                // has been closed and cleaned up.
                using (var c = utils.DefaultTestConnection)
                {
                    c.Close();
                    Thread.Sleep(500);
                }

                // attempt to test that dispose works while the connection close
                // has passed off work to cleanup the callback scheduler, etc.
                using (var c = utils.DefaultTestConnection)
                {
                    c.Close();
                    Thread.Sleep(500);
                }

                // Check that dispose is idempotent.
                using (var c = utils.DefaultTestConnection)
                {
                    c.Dispose();
                }
            }
        }

        [Fact]
        public void TestUserPassTokenOptions()
        {
            using (new NATSServer("-p 4444 --auth foo"))
            {
                Options opts = utils.DefaultTestOptions;

                opts.Url = "nats://localhost:4444";
                opts.Token = "foo";
                var c = new ConnectionFactory().CreateConnection(opts);
                c.Close();

                opts.Token = "garbage";
                Assert.Throws<NATSConnectionException>(() => { new ConnectionFactory().CreateConnection(opts); });
            }

            using (new NATSServer("-p 4444 --user foo --pass b@r"))
            {
                Options opts = utils.DefaultTestOptions;

                opts.Url = "nats://localhost:4444";
                opts.User = "foo";
                opts.Password = "b@r";
                var c = new ConnectionFactory().CreateConnection(opts);
                c.Close();

                opts.Password = "garbage";
                Assert.Throws<NATSConnectionException>(() => { new ConnectionFactory().CreateConnection(opts); });

                opts.User = "baz";
                opts.Password = "bar";
                Assert.Throws<NATSConnectionException>(() => { new ConnectionFactory().CreateConnection(opts); });
            }
        }

        [Fact]
        public void TestGenerateUniqueInboxNames()
        {
            using (new NATSServer())
            {
                string lastInboxName = null;

                for (var i = 0; i < 1000; i++)
                {
                    IConnection c = utils.DefaultTestConnection;
                    var inboxName = c.NewInbox();
                    c.Close();
                    Assert.NotEqual(inboxName, lastInboxName);
                    lastInboxName = inboxName;
                }
            }
        }

        [Fact]
        public void TestInfineReconnect()
        {
            var reconnectEv = new AutoResetEvent(false);
            var closedEv = new AutoResetEvent(false);
            
            var opts = ConnectionFactory.GetDefaultOptions();

            opts.Timeout = 500;
            opts.ReconnectWait = 10;
            opts.ReconnectedEventHandler = (obj, args) =>
            {
                reconnectEv.Set();
            };
            opts.ClosedEventHandler = (obj, args) =>
            {
                closedEv.Set();
            };

            using (var s = new NATSServer())
            {
                // first test a reconnect failure...
                opts.MaxReconnect = 1;
                using (var c = new ConnectionFactory().CreateConnection(opts))
                {
                    // we should just close - our one reconnect attempt failed.
                    s.Bounce(opts.Timeout * (opts.MaxReconnect + 1) + 500);

                    // we are closed, and not reconnected.
                    Assert.True(closedEv.WaitOne(10000));
                    Assert.False(reconnectEv.WaitOne(100));
                }
            }

            closedEv.Reset();
            reconnectEv.Reset();

            using (var s = new NATSServer())
            {
                // reconnect forever...
                opts.MaxReconnect = Options.ReconnectForever;
                using (var c = new ConnectionFactory().CreateConnection(opts))
                {
                    // with a timeout of 10ms, and a reconnectWait of 10 ms, we should have many
                    // reconnect attempts.
                    s.Bounce(20000);

                    // Assert that we reconnected and are not closed.
                    Assert.True(reconnectEv.WaitOne(10000));
                    Assert.False(closedEv.WaitOne(100));
                }
            }
        }

#if memcheck
        [Fact]
        public void TestMemoryLeakRequestReplyAsync()
        {
            using (new NATSServer())
            {
                using (var c = new ConnectionFactory().CreateConnection("nats://127.0.0.1:4222"))
                {
                    var data = new byte[102400];
                    var subject = "subject";

                    var startMem = GC.GetTotalMemory(true);

                    c.SubscribeAsync(subject, (sender, args) =>
                    {
                        c.Publish(args.Message.Reply, data);
                        c.Flush();
                    });

                    for (int i = 0; i < 100; i++)
                    {
                        var msg = c.Request(subject, data, int.MaxValue);
                    }
                    GC.Collect();
                    Thread.Sleep(5000);

                    double memGrowthPercent = 100 * (((double)(GC.GetTotalMemory(false) - startMem)) / (double)startMem);
                    Assert.True(memGrowthPercent < 30.0, string.Format("Memory grew {0} percent.", memGrowthPercent));

                    startMem = GC.GetTotalMemory(true);
                    for (int i = 0; i < 100; i++)
                    {
                        c.Request(subject, data);
                    }
                    GC.Collect();
                    Thread.Sleep(5000);

                    memGrowthPercent = 100 * (((double)(GC.GetTotalMemory(false) - startMem)) / (double)startMem);
                    Assert.True(memGrowthPercent < 30.0, string.Format("Memory grew {0} percent.", memGrowthPercent));

                    startMem = GC.GetTotalMemory(true);
                    var token = new CancellationToken();
                    for (int i = 0; i < 100; i++)
                    {
                        var t = c.RequestAsync(subject, data, int.MaxValue, token);
                        t.Wait();
                    }
                    GC.Collect();
                    Thread.Sleep(5000);

                    memGrowthPercent = 100 * (((double)(GC.GetTotalMemory(false) - startMem)) / (double)startMem);
                    Assert.True(memGrowthPercent < 30.0, string.Format("Memory grew {0} percent.", memGrowthPercent));
                }
            }
        }
#endif 

        [Fact]
        public void TestDrain()
        {
            using (new NATSServer())
            {
                var c = utils.DefaultTestConnection;

                AutoResetEvent done = new AutoResetEvent(false);
                int received = 0;
                int expected = 10;

                var s = c.SubscribeAsync("foo", (obj, args) =>
                {
                    // allow messages to back up
                    Thread.Sleep(100);

                    int count = Interlocked.Increment(ref received);
                    if (count == expected)
                    {
                        done.Set();
                    }
                });

                for (int i = 0; i < expected; i++)
                {
                    c.Publish("foo", null);
                }
                c.Drain();

                done.WaitOne(5000);
                Assert.True(received == expected, string.Format("recieved {0} of {1}", received, expected));
            }
        }

        [Fact]
        public void TestDrainAsync()
        {
            using (new NATSServer())
            {
                var c = utils.DefaultTestConnection;

                AutoResetEvent done = new AutoResetEvent(false);
                int received = 0;
                int expected = 10;

                var s = c.SubscribeAsync("foo", (obj, args) =>
                {
                    // allow messages to back up
                    Thread.Sleep(250);

                    int count = Interlocked.Increment(ref received);
                    if (count == expected)
                    {
                        done.Set();
                    }
                });

                for (int i = 0; i < expected; i++)
                {
                    c.Publish("foo", null);
                }
                var sw = Stopwatch.StartNew();
                var t = c.DrainAsync();
                sw.Stop();

                // are we really async?
                Assert.True(sw.ElapsedMilliseconds < 2500);
                t.Wait();

                done.WaitOne(5000);
                Assert.True(received == expected, string.Format("recieved {0} of {1}", received, expected));
            }
        }

        [Fact]
        public void TestDrainSub()
        {
            using (new NATSServer())
            {
                var c = utils.DefaultTestConnection;

                AutoResetEvent done = new AutoResetEvent(false);
                int received = 0;
                int expected = 10;

                var s = c.SubscribeAsync("foo", (obj, args) =>
                {
                    // allow messages to back up
                    Thread.Sleep(100);

                    int count = Interlocked.Increment(ref received);
                    if (count == expected)
                    {
                        done.Set();
                    }
                });

                for (int i = 0; i < expected; i++)
                {
                    c.Publish("foo", null);
                }
                s.Drain();

                done.WaitOne(5000);
                Assert.True(received == expected, string.Format("recieved {0} of {1}", received, expected));
            }
        }

        [Fact]
        public void TestDrainSubAsync()
        {
            using (new NATSServer())
            {
                var c = utils.DefaultTestConnection;

                AutoResetEvent done = new AutoResetEvent(false);
                int received = 0;
                int expected = 10;

                var s = c.SubscribeAsync("foo", (obj, args) =>
                {
                    // allow messages to back up
                    Thread.Sleep(100);

                    int count = Interlocked.Increment(ref received);
                    if (count == expected)
                    {
                        done.Set();
                    }
                });

                for (int i = 0; i < expected; i++)
                {
                    c.Publish("foo", null);
                }
                var sw = Stopwatch.StartNew();
                var t = s.DrainAsync();
                sw.Stop();

                // are we really async?
                Assert.True(sw.ElapsedMilliseconds < 1000);
                t.Wait();

                done.WaitOne(5000);
                Assert.True(received == expected, string.Format("recieved {0} of {1}", received, expected));
            }
        }

        [Fact]
        public void TestDrainBadParams()
        {
            using (new NATSServer())
            {
                var c = utils.DefaultTestConnection;
                var s = c.SubscribeAsync("foo");
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => c.DrainAsync(-1));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => c.DrainAsync(0));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => s.DrainAsync(-1));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => s.DrainAsync(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => c.Drain(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => c.Drain(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Drain(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Drain(0));
            }
        }

        [Fact]
        public void TestDrainTimeoutAsync()
        {
            using (new NATSServer())
            {
                var c = utils.DefaultTestConnection;
                var s = c.SubscribeAsync("foo", (obj, args) =>
                {
                    // allow about 30s of messages to back up
                    Thread.Sleep(1000);
                });

                for (int i = 0; i < 30; i++)
                {
                    c.Publish("foo", null);
                }
                Stopwatch sw = Stopwatch.StartNew();
                var t = c.DrainAsync(1000);
                try
                {
                    t.Wait();
                }
                catch (Exception)
                {
                    // timed out.
                }
                sw.Stop();


                // add slack for slow CI.
                Assert.True(sw.ElapsedMilliseconds >= 1000);
            }
        }

        [Fact]
        public void TestDrainBlocking()
        {
            using (new NATSServer())
            {
                var c = utils.DefaultTestConnection;
                var s = c.SubscribeAsync("foo", (obj, args) =>
                {
                    // allow about 30s of messages to back up
                    Thread.Sleep(100);
                });

                for (int i = 0; i < 30; i++)
                {
                    c.Publish("foo", null);
                }
                Stopwatch sw = Stopwatch.StartNew();
                Assert.Throws<NATSTimeoutException>(() => c.Drain(500));
                sw.Stop();

                // add slack for slow CI.
                Assert.True(sw.ElapsedMilliseconds >= 500);
            }
        }

        [Fact]
        public void TestSubDrainBlockingTimeout()
        {
            using (new NATSServer())
            {
                bool aehHit = false;
                AutoResetEvent ev = new AutoResetEvent(false);

                var opts = ConnectionFactory.GetDefaultOptions();
                opts.AsyncErrorEventHandler = (obj, args) =>
                {
                    aehHit = args.Error.Contains("Drain");
                    ev.Set();
                };

                var c = new ConnectionFactory().CreateConnection(opts);
                var s = c.SubscribeAsync("foo", (obj, args) =>
                {
                    // allow about 30s of messages to back up
                    Thread.Sleep(1000);
                });

                for (int i = 0; i < 30; i++)
                {
                    c.Publish("foo", null);
                }
                Stopwatch sw = Stopwatch.StartNew();
                Assert.Throws<NATSTimeoutException>(()=> s.Drain(500));
                sw.Stop();

                // add slack for slow CI.
                Assert.True(sw.ElapsedMilliseconds >= 500);
                Assert.True(ev.WaitOne(4000));
                Assert.True(aehHit);
            }
        }


        [Fact]
        public void TestDrainStateBehavior()
        {
            using (new NATSServer())
            {
                AutoResetEvent closed = new AutoResetEvent(false);

                var opts = ConnectionFactory.GetDefaultOptions();
                opts.ClosedEventHandler = (obj, args) =>
                {
                    closed.Set(); 
                };
                var c = new ConnectionFactory().CreateConnection(opts);
                var s = c.SubscribeAsync("foo", (obj, args) =>
                {
                    // allow about 5s of messages to back up
                    Thread.Sleep(500);
                });

                for (int i = 0; i < 10; i++)
                {
                    c.Publish("foo", null);
                }
                // give us a long timeout to run our test.
                c.DrainAsync(10000);

                Assert.True(c.State == ConnState.DRAINING_SUBS);
                Assert.True(c.IsDraining());

                Assert.Throws<NATSConnectionDrainingException>(() => c.SubscribeAsync("foo"));
                Assert.Throws<NATSConnectionDrainingException>(() => c.SubscribeSync("foo"));

                // Make sure we hit connection closed.
                Assert.True(closed.WaitOne(10000));
            }
        }

        /// NOT IMPLEMENTED:
        /// TestServerSecureConnections
        /// TestErrOnConnectAndDeadlock
        /// TestErrOnMaxPayloadLimit

    } // class

} // namespace
