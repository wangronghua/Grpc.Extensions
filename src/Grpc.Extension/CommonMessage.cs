using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace Grpc.Extension
{
    //[ProtoContract]
    //public class EmptyMessage
    //{
    //}

    //[ProtoContract]
    //public class BoolMessage
    //{
    //    [ProtoMember(1)]
    //    public bool Value { get; set; }
    //}

    //[ProtoContract]
    //public class IntMessage
    //{
    //    [ProtoMember(1)]
    //    public int Value { get; set; }
    //}

    //[ProtoContract]
    //public class DoubleMessage
    //{
    //    [ProtoMember(1)]
    //    public double Value { get; set; }
    //}

    //[ProtoContract]
    //public class StringMessage
    //{
    //    [ProtoMember(1)]
    //    public string Value { get; set; }
    //}
    [ProtoContract]
    public class GrpcRequestMessage<T>
    {
        [ProtoMember(1)]
        public T Value { get; set; }
    }
    [ProtoContract]
    public class GrpcResponseMessage<T> : IGrpcResponseMessage
    {
        [ProtoMember(1)]
        public string ErrorStr { get; set; }

        [ProtoMember(2)]
        public T Value { get; set; }
    }
    [ProtoContract]
    public class GrpcResponseMessage : IGrpcResponseMessage
    {
        [ProtoMember(1)]
        public string ErrorStr { get; set; }
    }
    public interface IGrpcResponseMessage
    {
        string ErrorStr { get; set; }
    }
}
