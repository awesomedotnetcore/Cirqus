﻿using System;
using System.Linq;
using System.Text;
using d60.Cirqus.Events;
using d60.Cirqus.Extensions;
using d60.Cirqus.Ntfs.Events;
using d60.Cirqus.Numbers;
using NUnit.Framework;

namespace d60.Cirqus.Tests.Events.Ntfs
{
    [TestFixture]
    public class TestNtfsEventStore : FixtureBase
    {
        NtfsEventStore _eventStore;

        protected override void DoSetUp()
        {
            _eventStore = RegisterForDisposal(new NtfsEventStore("testdata", dropEvents: true));
        }

        [Test]
        public void OnlyReadCommittedOnLoad()
        {
            var rootId = Guid.NewGuid();

            // make one full commit
            _eventStore.Save(rootId, new[]
            {
                EventData.FromMetadata(new Metadata
                {
                    {DomainEvent.MetadataKeys.SequenceNumber, 0.ToString(Metadata.NumberCulture)},
                    {DomainEvent.MetadataKeys.AggregateRootId, rootId.ToString()}

                }, new byte[0])
            });

            // save an event to a file, without committing
            _eventStore.DataStore.Write(
                EventData.FromMetadata(new Metadata
                {
                    {DomainEvent.MetadataKeys.SequenceNumber, 1.ToString(Metadata.NumberCulture)},
                    {DomainEvent.MetadataKeys.AggregateRootId, rootId.ToString()},
                    {DomainEvent.MetadataKeys.GlobalSequenceNumber, 1.ToString(Metadata.NumberCulture)}
                }, new byte[0]));


            var events = _eventStore.Load(rootId);
            Assert.AreEqual(1, events.Count());
        }

        [Test]
        public void OnlyReadCommittedOnStream()
        {
            var rootId = Guid.NewGuid();

            // make one full commit
            _eventStore.Save(rootId, new[]
            {
                EventData.FromMetadata(new Metadata
                {
                    {DomainEvent.MetadataKeys.SequenceNumber, 0.ToString(Metadata.NumberCulture)},
                    {DomainEvent.MetadataKeys.AggregateRootId, rootId.ToString()}
                }, new byte[0])
            });

            // save an event to sequence-index, without committing
            _eventStore.GlobalSequenceIndex.Write(new[]
            {
                new GlobalSequenceIndex.GlobalSequenceRecord
                {
                    GlobalSequenceNumber = 1,
                    AggregateRootId = rootId,
                    LocalSequenceNumber = 1
                }
            });

            var events = _eventStore.Stream();
            Assert.AreEqual(1, events.Count());
        }

        [Test]
        public void CanRecoverAfterWritingIndex()
        {
            var rootId = Guid.NewGuid();

            // make one full commit
            _eventStore.Save(rootId, new[]
            {
                EventData.FromMetadata(new Metadata
                {
                    {DomainEvent.MetadataKeys.SequenceNumber, 0.ToString(Metadata.NumberCulture)},
                    {DomainEvent.MetadataKeys.AggregateRootId, rootId.ToString()}
                }, new byte[0])
            });

            // make one that fails right after index write
            _eventStore.GlobalSequenceIndex.Write(new[]
            {
                new GlobalSequenceIndex.GlobalSequenceRecord
                {
                    GlobalSequenceNumber = 1,
                    AggregateRootId = rootId,
                    LocalSequenceNumber = 1
                }
            });

            // make one full commit
            _eventStore.Save(rootId, new[]
            {
                EventData.FromMetadata(new Metadata
                {
                    {DomainEvent.MetadataKeys.SequenceNumber, 1.ToString(Metadata.NumberCulture)},
                    {DomainEvent.MetadataKeys.AggregateRootId, rootId.ToString()}
                }, new byte[0])
            });

            var stream = _eventStore.Stream().ToList();
            Assert.AreEqual(1, stream.Last().GetGlobalSequenceNumber());
            Assert.AreEqual(2, stream.Count());

            var load = _eventStore.Load(rootId);
            Assert.AreEqual(2, load.Count());
        }

        [Test]
        public void CanRecoverAfterSavingEventData()
        {
            var rootId = Guid.NewGuid();

            // make one full commit
            _eventStore.Save(rootId, new[]
            {
                EventData.FromMetadata(new Metadata
                {
                    {DomainEvent.MetadataKeys.SequenceNumber, 0.ToString(Metadata.NumberCulture)},
                    {DomainEvent.MetadataKeys.AggregateRootId, rootId.ToString()}
                }, new byte[0])
            });

            // make one that fails right after index write
            var domainEvent = EventData.FromMetadata(new Metadata
            {
                {DomainEvent.MetadataKeys.SequenceNumber, 1.ToString(Metadata.NumberCulture)},
                {DomainEvent.MetadataKeys.AggregateRootId, rootId.ToString()},
                {DomainEvent.MetadataKeys.GlobalSequenceNumber, 1.ToString(Metadata.NumberCulture)}
            }, Encoding.UTF8.GetBytes("The bad one"));

            _eventStore.GlobalSequenceIndex.Write(new[] { domainEvent });
            _eventStore.DataStore.Write(domainEvent);

            // make one full commit
            _eventStore.Save(rootId, new[]
            {
                EventData.FromMetadata(new Metadata
                {
                    {DomainEvent.MetadataKeys.SequenceNumber, 1.ToString(Metadata.NumberCulture)},
                    {DomainEvent.MetadataKeys.AggregateRootId, rootId.ToString()}
                }, Encoding.UTF8.GetBytes("The good one"))
            });

            var stream = _eventStore.Stream().ToList();
            Assert.AreEqual(2, stream.Count());
            Assert.AreEqual(1, stream.Last().GetGlobalSequenceNumber());

            var load = _eventStore.Load(rootId).ToList();
            Assert.AreEqual(2, load.Count());
            Assert.AreEqual("The good one", Encoding.UTF8.GetString(load.Last().Data));
        }
    }
}