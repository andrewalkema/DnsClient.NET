﻿using System;
using System.Collections.Generic;
using DnsClient.Protocol;
using DnsClient.Protocol.Options;

namespace DnsClient
{
    internal class DnsRecordFactory
    {
        private readonly DnsDatagramReader _reader;

        public DnsRecordFactory(DnsDatagramReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            _reader = reader;
        }

        /*
        0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                                               |
        /                                               /
        /                      NAME                     /
        |                                               |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                      TYPE                     |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                     CLASS                     |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                      TTL                      |
        |                                               |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
        |                   RDLENGTH                    |
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--|
        /                     RDATA                     /
        /                                               /
        +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
         * */
        public ResourceRecordInfo ReadRecordInfo()
        {
            return new ResourceRecordInfo(
                _reader.ReadName(),                                     // name
                (ResourceRecordType)_reader.ReadUInt16NetworkOrder(),   // type
                (QueryClass)_reader.ReadUInt16NetworkOrder(),           // class
                (int)_reader.ReadUInt32NetworkOrder(),                  // ttl - 32bit!!
                _reader.ReadUInt16NetworkOrder());                      // RDLength
        }

        public DnsResourceRecord GetRecord(ResourceRecordInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            var oldIndex = _reader.Index;
            DnsResourceRecord result;

            switch (info.RecordType)
            {
                case ResourceRecordType.A:
                    result = new ARecord(info, _reader.ReadIPAddress());
                    break;

                case ResourceRecordType.NS:
                    result = new NsRecord(info, _reader.ReadName());
                    break;

                case ResourceRecordType.CNAME:
                    result = new CNameRecord(info, _reader.ReadName());
                    break;

                case ResourceRecordType.SOA:
                    result = ResolveSoaRecord(info);
                    break;

                case ResourceRecordType.MB:
                    result = new MbRecord(info, _reader.ReadName());
                    break;

                case ResourceRecordType.MG:
                    result = new MgRecord(info, _reader.ReadName());
                    break;

                case ResourceRecordType.MR:
                    result = new MrRecord(info, _reader.ReadName());
                    break;

                case ResourceRecordType.NULL:
                    result = new NullRecord(info, _reader.ReadBytes(info.RawDataLength));
                    break;

                case ResourceRecordType.WKS:
                    result = ResolveWksRecord(info);
                    break;

                case ResourceRecordType.PTR:
                    result = new PtrRecord(info, _reader.ReadName());
                    break;

                case ResourceRecordType.HINFO:
                    result = new HInfoRecord(info, _reader.ReadString(), _reader.ReadString());
                    break;

                case ResourceRecordType.MINFO:
                    result = new MInfoRecord(info, _reader.ReadName(), _reader.ReadName());
                    break;

                case ResourceRecordType.MX:
                    result = ResolveMXRecord(info);
                    break;

                case ResourceRecordType.TXT:
                    result = ResolveTXTRecord(info);
                    break;

                case ResourceRecordType.RP:
                    result = new RpRecord(info, _reader.ReadName(), _reader.ReadName());
                    break;

                case ResourceRecordType.AFSDB:
                    result = new AfsDbRecord(info, (AfsType)_reader.ReadUInt16NetworkOrder(), _reader.ReadName());
                    break;

                case ResourceRecordType.AAAA:
                    result = new AaaaRecord(info, _reader.ReadIPv6Address());
                    break;

                case ResourceRecordType.SRV:
                    result = ResolveSrvRecord(info);
                    break;

                case ResourceRecordType.OPT:
                    result = ResolveOptRecord(info);
                    break;

                case ResourceRecordType.CAA:
                    result = ResolveCaaRecord(info);
                    break;

                default:
                    // update reader index because we don't read full data for the empty record
                    _reader.Index += info.RawDataLength;
                    result = new EmptyRecord(info);
                    break;
            }

            // sanity check
            if (_reader.Index != oldIndex + info.RawDataLength)
            {
                throw new InvalidOperationException("Record reader index out of sync.");
            }

            return result;
        }

        private DnsResourceRecord ResolveOptRecord(ResourceRecordInfo info)
        {
            return new OptRecord((int)info.RecordClass, info.TimeToLive, info.RawDataLength);
        }

        private DnsResourceRecord ResolveWksRecord(ResourceRecordInfo info)
        {
            var address = _reader.ReadIPAddress();
            var protocol = _reader.ReadByte();
            var bitmap = _reader.ReadBytes(info.RawDataLength - 5);

            return new WksRecord(info, address, protocol, bitmap);
        }

        private DnsResourceRecord ResolveMXRecord(ResourceRecordInfo info)
        {
            var preference = _reader.ReadUInt16NetworkOrder();
            var domain = _reader.ReadName();

            return new MxRecord(info, preference, domain);
        }

        private DnsResourceRecord ResolveSoaRecord(ResourceRecordInfo info)
        {
            var mName = _reader.ReadName();
            var rName = _reader.ReadName();
            var serial = _reader.ReadUInt32NetworkOrder();
            var refresh = _reader.ReadUInt32NetworkOrder();
            var retry = _reader.ReadUInt32NetworkOrder();
            var expire = _reader.ReadUInt32NetworkOrder();
            var minimum = _reader.ReadUInt32NetworkOrder();

            return new SoaRecord(info, mName, rName, serial, refresh, retry, expire, minimum);
        }

        private DnsResourceRecord ResolveSrvRecord(ResourceRecordInfo info)
        {
            var priority = _reader.ReadUInt16NetworkOrder();
            var weight = _reader.ReadUInt16NetworkOrder();
            var port = _reader.ReadUInt16NetworkOrder();
            var target = _reader.ReadName();

            return new SrvRecord(info, priority, weight, port, target);
        }

        private DnsResourceRecord ResolveTXTRecord(ResourceRecordInfo info)
        {
            int pos = _reader.Index;

            var values = new List<string>();
            var utf8Values = new List<string>();
            while ((_reader.Index - pos) < info.RawDataLength)
            {
                var length = _reader.ReadByte();
                var bytes = _reader.ReadBytes(length);
                var escaped = DnsDatagramReader.ParseString(bytes, 0, length);
                var utf = DnsDatagramReader.ReadUTF8String(bytes, 0, length);
                values.Add(escaped);
                utf8Values.Add(utf);
            }

            return new TxtRecord(info, values.ToArray(), utf8Values.ToArray());
        }

        private DnsResourceRecord ResolveCaaRecord(ResourceRecordInfo info)
        {
            var flag = _reader.ReadByte();
            var tag = _reader.ReadString();
            var stringValue = DnsDatagramReader.ParseString(_reader, info.RawDataLength - 2 - tag.Length);
            return new CaaRecord(info, flag, tag, stringValue);
        }
    }
}