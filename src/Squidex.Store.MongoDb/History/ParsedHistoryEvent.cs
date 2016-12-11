﻿// ==========================================================================
//  ParsedHistoryEvent.cs
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex Group
//  All rights reserved.
// ==========================================================================

using System;
using System.Collections.Generic;
using Squidex.Read.History;

namespace Squidex.Store.MongoDb.History
{
    internal sealed class ParsedHistoryEvent : IHistoryEventEntity
    {
        private readonly MongoHistoryEventEntity inner;
        private readonly Lazy<string> message;

        public Guid Id
        {
            get { return inner.Id; }
        }

        public DateTime Created
        {
            get { return inner.Created; }
        }

        public DateTime LastModified
        {
            get { return inner.LastModified; }
        }

        public string Channel
        {
            get { return inner.Channel; }
        }

        public string Message
        {
            get { return message.Value; }
        }

        public ParsedHistoryEvent(MongoHistoryEventEntity inner, IReadOnlyDictionary<string, string> texts)
        {
            this.inner = inner;

            message = new Lazy<string>(() =>
            {
                var result = texts[inner.Message];

                foreach (var kvp in inner.Parameters)
                {
                    result = result.Replace("[" + kvp.Key + "]", kvp.Value);
                }

                return result;
            });
        }
    }
}