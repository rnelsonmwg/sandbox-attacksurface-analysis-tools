﻿//  Copyright 2016 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace NtApiDotNet
{
#pragma warning disable 1591
    public static partial class NtRtl
    {
        [DllImport("ntdll.dll")]
        public static extern void RtlZeroMemory(
            IntPtr Destination,
            IntPtr Length
        );

        [DllImport("ntdll.dll")]
        public static extern void RtlFillMemory(
            IntPtr Destination,
            IntPtr Length,
            byte Fill
        );
    }

    /// <summary>
    /// A safe handle to an allocated global buffer.
    /// </summary>
    public class SafeHGlobalBuffer : SafeBuffer
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="length">Size of the buffer to allocate.</param>
        public SafeHGlobalBuffer(int length)
          : this(length, length)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="allocation_length">The length of data to allocate.</param>
        /// <param name="total_length">The total length to reflect in the Length property.</param>
        protected SafeHGlobalBuffer(int allocation_length, int total_length) 
            : this(Marshal.AllocHGlobal(allocation_length), total_length, true)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="length">Size of the buffer.</param>
        /// <param name="buffer">An existing pointer to an existing HGLOBAL allocated buffer.</param>
        /// <param name="owns_handle">Specify whether safe handle owns the buffer.</param>
        public SafeHGlobalBuffer(IntPtr buffer, int length, bool owns_handle)
          : base(owns_handle)
        {
            Length = length;
            Initialize((ulong)length);
            SetHandle(buffer);
        }

        public static SafeHGlobalBuffer Null { get { return new SafeHGlobalBuffer(IntPtr.Zero, 0, false); } }

        [ReliabilityContract(Consistency.MayCorruptInstance, Cer.None)]
        public virtual void Resize(int new_length)
        {
            IntPtr free_handle = IntPtr.Zero;
            try
            {
                byte[] old_data = new byte[Length];
                Marshal.Copy(handle, old_data, 0, Length);
                free_handle = Marshal.AllocHGlobal(new_length);
                Marshal.Copy(old_data, 0, free_handle, Math.Min(new_length, Length));
                free_handle = Interlocked.Exchange(ref handle, free_handle);
                Length = new_length;
                Initialize((ulong)new_length);
            }
            finally
            {
                if (free_handle != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(free_handle);
                }
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="data">Initialization data for the buffer.</param>
        public SafeHGlobalBuffer(byte[] data) : this(data.Length)
        {
            Marshal.Copy(data, 0, handle, data.Length);
        }

        public int Length
        {
            get; private set;
        }

        /// <summary>
        /// Get the length as an IntPtr
        /// </summary>
        public IntPtr LengthIntPtr
        {
            get { return new IntPtr(Length); }
        }

        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                Marshal.FreeHGlobal(handle);
                handle = IntPtr.Zero;
            }
            return true;
        }

        /// <summary>
        /// Convert the safe handle to an array of bytes.
        /// </summary>
        /// <returns>The data contained in the allocaiton.</returns>
        public byte[] ToArray()
        {
            return ReadBytes(Length);
        }

        /// <summary>
        /// Read a NUL terminated string for the byte offset.
        /// </summary>
        /// <param name="byte_offset">The byte offset to read from.</param>
        /// <returns>The string read from the buffer without the NUL terminator</returns>
        public string ReadNulTerminatedUnicodeString(ulong byte_offset)
        {
            return BufferUtils.ReadNulTerminatedUnicodeString(this, byte_offset);
        }

        /// <summary>
        /// Read a NUL terminated string
        /// </summary>
        /// <returns>The string read from the buffer without the NUL terminator</returns>
        public string ReadNulTerminatedUnicodeString()
        {
            return ReadNulTerminatedUnicodeString(0);
        }

        public string ReadUnicodeString(ulong byte_offset, int count)
        {
            return BufferUtils.ReadUnicodeString(this, byte_offset, count);
        }

        public string ReadUnicodeString(int count)
        {
            return ReadUnicodeString(0, count);
        }

        public void WriteUnicodeString(ulong byte_offset, string value)
        {
            BufferUtils.WriteUnicodeString(this, byte_offset, value);
        }

        public void WriteUnicodeString(string value)
        {
            WriteUnicodeString(0, value);
        }

        public byte[] ReadBytes(ulong byte_offset, int count)
        {
            return BufferUtils.ReadBytes(this, byte_offset, count);
        }

        public byte[] ReadBytes(int count)
        {
            return ReadBytes(0, count);
        }

        public void WriteBytes(ulong byte_offset, byte[] data)
        {
            BufferUtils.WriteBytes(this, byte_offset, data);
        }

        public void WriteBytes(byte[] data)
        {
            WriteBytes(0, data);
        }

        /// <summary>
        /// Zero an entire buffer.
        /// </summary>
        public void ZeroBuffer()
        {
            BufferUtils.ZeroBuffer(this);
        }

        /// <summary>
        /// Fill an entire buffer with a specific byte value.
        /// </summary>
        /// <param name="fill">The fill value.</param>
        public void FillBuffer(byte fill)
        {
            BufferUtils.FillBuffer(this, fill);
        }

        public SafeStructureInOutBuffer<T> GetStructAtOffset<T>(int offset) where T : new()
        {
            return BufferUtils.GetStructAtOffset<T>(this, offset);
        }

        /// <summary>
        /// Detaches the current buffer and allocates a new one.
        /// </summary>
        /// <returns>The detached buffer.</returns>
        /// <remarks>The original buffer will become invalid after this call.</remarks>
        [ReliabilityContract(Consistency.MayCorruptInstance, Cer.MayFail)]
        public SafeHGlobalBuffer Detach()
        {
            return Detach(Length);
        }

        /// <summary>
        /// Detaches the current buffer and allocates a new one.
        /// </summary>
        /// <param name="length">Specify a new length for the detached buffer. Must be &lt;= Length.</param>
        /// <returns>The detached buffer.</returns>
        /// <remarks>The original buffer will become invalid after this call.</remarks>
        [ReliabilityContract(Consistency.MayCorruptInstance, Cer.MayFail)]
        public SafeHGlobalBuffer Detach(int length)
        {
            if (length > Length)
            {
                throw new ArgumentException("Buffer length is smaller than new length");
            }

            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                IntPtr handle = DangerousGetHandle();
                SetHandleAsInvalid();
                return new SafeHGlobalBuffer(handle, length, true);
            }
            finally
            {
            }
        }
    }

    /// <summary>
    /// Safe handle for an in/out structure buffer.
    /// </summary>
    /// <typeparam name="T">The type of structure as the base of the memory allocation.</typeparam>
    public class SafeStructureInOutBuffer<T> : SafeHGlobalBuffer where T : new()
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="value">Structure value to initialize the buffer.</param>
        public SafeStructureInOutBuffer(T value)
            : this(value, 0, true)
        {
        }

        /// <summary>
        /// Constructor, initializes buffer with a default structure.
        /// </summary>        
        public SafeStructureInOutBuffer()
            : this(new T(), 0, true)
        {
        }

        public SafeStructureInOutBuffer(IntPtr buffer, int length, bool owns_handle) 
            : base(buffer, length, owns_handle)
        {
        }

        // Private constructor for Null buffer.
        protected SafeStructureInOutBuffer(int dummy_length) : base(IntPtr.Zero, dummy_length, false)
        {
        }

        new public static SafeStructureInOutBuffer<T> Null { get { return new SafeStructureInOutBuffer<T>(0); } }

        /// <summary>
        /// Constructor, initializes buffer with a default structure.
        /// </summary>
        /// <param name="additional_size">Additional data to add to structure buffer.</param>
        /// <param name="add_struct_size">If true additional_size is added to structure size, otherwise reflects the total size.</param>
        public SafeStructureInOutBuffer(int additional_size, bool add_struct_size)
            : this(new T(), additional_size, add_struct_size)
        {
        }

        private static int GetTotalLength(int additional_size, bool add_struct_size)
        {
            if (add_struct_size)
            {
                int data_offset = BufferUtils.GetIncludeField<T>() 
                    ? Marshal.SizeOf(typeof(T)) : BufferUtils.GetStructDataOffset<T>();
                return data_offset + additional_size;
            }
            return additional_size;
        }

        private static int GetAllocationLength(int length)
        {
            // Always ensure we at least allocate the entire structure length.
            return Math.Max(Marshal.SizeOf(typeof(T)), length);
        }

        private SafeStructureInOutBuffer(T value, int total_length) 
            : base(GetAllocationLength(total_length), total_length)
        {
            Marshal.StructureToPtr(value, handle, false);
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="value">Structure value to initialize the buffer.</param>
        /// <param name="additional_size">Additional data to add to structure buffer.</param>
        /// <param name="add_struct_size">If true additional_size is added to structure size, otherwise reflects the total size.</param>
        public SafeStructureInOutBuffer(T value, int additional_size, bool add_struct_size)
            : this(value, GetTotalLength(additional_size, add_struct_size))
        {
        }

        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                Marshal.DestroyStructure(handle, typeof(T));
            }
            return base.ReleaseHandle();
        }

        /// <summary>
        /// Convert the buffer back to a structure.
        /// </summary>
        public virtual T Result
        {
            get
            {
                if (IsClosed || IsInvalid)
                    throw new ObjectDisposedException("handle");
                
                return (T)Marshal.PtrToStructure(handle, typeof(T));
            }
        }

        /// <summary>
        /// Get a reference to the additional data.
        /// </summary>
        public SafeHGlobalBuffer Data
        {
            get
            {
                if (IsClosed || IsInvalid)
                    throw new ObjectDisposedException("handle");

                int size = BufferUtils.GetStructDataOffset<T>();
                int length = Length - size;
                return new SafeHGlobalBuffer(handle + size, length < 0 ? 0 : length, false);
            }
        }

        /// <summary>
        /// Detaches the current buffer and allocates a new one.
        /// </summary>
        /// <returns>The detached buffer.</returns>
        /// <remarks>The original buffer will become invalid after this call.</remarks>
        [ReliabilityContract(Consistency.MayCorruptInstance, Cer.MayFail)]
        new public SafeStructureInOutBuffer<T> Detach()
        {
            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                IntPtr handle = DangerousGetHandle();
                SetHandleAsInvalid();
                return new SafeStructureInOutBuffer<T>(handle, Length, true);
            }
            finally
            {
            }
        }
    }

    public class SafeStructureArrayBuffer<T> : SafeStructureInOutBuffer<T> where T : new()
    {
        private int _array_length;

        private static FieldInfo GetDataStartField()
        {
            DataStartAttribute attr = typeof(T).GetCustomAttribute<DataStartAttribute>();
            if (attr != null)
            {
                return typeof(T).GetField(attr.FieldName);
            }
            return null;
        }

        private static Array GetArray(T value)
        {
            FieldInfo fi = GetDataStartField();
            if (fi == null)
            {
                throw new ArgumentException("Structure must contain a data start field");
            }
            Type field_type = fi.FieldType;
            if (!field_type.IsArray && !field_type.GetElementType().IsValueType)
            {
                throw new ArgumentException("Data start field must be an array of a value type");
            }

            Array array = (Array)fi.GetValue(value);
            if (array == null)
            {
                throw new ArgumentNullException("Data array must not be null");
            }
            return array;
        }

        private static int CalculateDataLength(Array array)
        {
            return array.Length * Marshal.SizeOf(array.GetType().GetElementType());
        }

        private SafeStructureArrayBuffer(T value, Array array) : base(value, CalculateDataLength(array), true)
        {
            _array_length = array.Length;
        }

        public SafeStructureArrayBuffer(T value) : this(value, GetArray(value))
        {
        }

        protected SafeStructureArrayBuffer(int dummy_length) : base(dummy_length)
        {
        }

        new public static SafeStructureArrayBuffer<T> Null { get { return new SafeStructureArrayBuffer<T>(0); } }

        public override T Result
        {
            get
            {
                T result = base.Result;
                FieldInfo fi = GetDataStartField();
                Type elem_type = fi.FieldType.GetElementType();
                Array array = Array.CreateInstance(elem_type, _array_length);
                IntPtr current_ptr = Data.DangerousGetHandle();
                int elem_size = Marshal.SizeOf(elem_type);
                for (int i = 0; i < _array_length; ++i)
                {
                    array.SetValue(Marshal.PtrToStructure(current_ptr, elem_type), i);
                    current_ptr += elem_size;
                }
                fi.SetValue(result, array);
                return result;
            }
        }
    }

    public sealed class SafeKernelObjectHandle
      : SafeHandle
    {
        private string _type_name;

        private SafeKernelObjectHandle()
            : base(IntPtr.Zero, true)
        {
        }

        public SafeKernelObjectHandle(IntPtr handle, bool owns_handle)
          : base(IntPtr.Zero, owns_handle)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            if (NtSystemCalls.NtClose(this.handle).IsSuccess())
            {
                this.handle = IntPtr.Zero;
                return true;
            }
            return false;
        }

        public override bool IsInvalid
        {
            get
            {
                return handle.ToInt64() <= 0;
            }
        }

        public static SafeKernelObjectHandle Null
        {
            get { return new SafeKernelObjectHandle(IntPtr.Zero, false); }
        }

        private ObjectHandleInformation QueryHandleInformation()
        {
            using (var buffer = new SafeStructureInOutBuffer<ObjectHandleInformation>())
            {
                int return_length;
                NtSystemCalls.NtQueryObject(this, ObjectInformationClass.ObjectHandleFlagInformation,
                    buffer, buffer.Length, out return_length).ToNtException();
                return buffer.Result;
            }
        }

        private void SetHandleInformation(ObjectHandleInformation handle_info)
        {
            using (var buffer = handle_info.ToBuffer())
            {
                NtSystemCalls.NtSetInformationObject(
                    this, ObjectInformationClass.ObjectHandleFlagInformation,
                    buffer, buffer.Length).ToNtException();
            }
        }

        /// <summary>
        /// Get or set whether the handle is inheritable.
        /// </summary>
        public bool Inherit
        {
            get
            {
                return QueryHandleInformation().Inherit;
            }

            set
            {
                var handle_info = QueryHandleInformation();
                handle_info.Inherit = value;
                SetHandleInformation(handle_info);
            }
        }

        /// <summary>
        /// Get or set whether the handle is protected from closing.
        /// </summary>
        public bool ProtectFromClose
        {
            get
            {
                return QueryHandleInformation().ProtectFromClose;
            }

            set
            {
                var handle_info = QueryHandleInformation();
                handle_info.ProtectFromClose = value;
                SetHandleInformation(handle_info);
            }
        }

        /// <summary>
        /// Get the NT type name for this handle.
        /// </summary>
        /// <returns>The NT type name.</returns>
        public string NtTypeName
        {
            get
            {
                if (_type_name == null)
                {
                    using (var type_info = new SafeStructureInOutBuffer<ObjectTypeInformation>(1024, true))
                    {
                        NtSystemCalls.NtQueryObject(this,
                            ObjectInformationClass.ObjectTypeInformation, type_info, 
                            type_info.Length, out int return_length).ToNtException();
                        _type_name = type_info.Result.Name.ToString();
                    }
                }
                return _type_name;
            }
        }

        /// <summary>
        /// Overridden ToString method.
        /// </summary>
        /// <returns>The handle as a string.</returns>
        public override string ToString()
        {
            return $"0x{DangerousGetHandle().ToInt64():X}";
        }
    }

    public sealed class SafeHandleListHandle : SafeHGlobalBuffer
    {
        private SafeKernelObjectHandle[] _handles;

        public SafeHandleListHandle(IEnumerable<SafeKernelObjectHandle> handles)
          : base(IntPtr.Size * handles.Count())
        {
            _handles = handles.ToArray();
            IntPtr buffer = handle;
            for (int i = 0; i < _handles.Length; ++i)
            {
                Marshal.WriteIntPtr(buffer, _handles[i].DangerousGetHandle());
                buffer += IntPtr.Size;
            }
        }

        protected override bool ReleaseHandle()
        {
            foreach (SafeKernelObjectHandle handle in _handles)
            {
                handle.Close();
            }
            _handles = new SafeKernelObjectHandle[0];
            return base.ReleaseHandle();
        }
    }

    public sealed class SafeStringBuffer : SafeHGlobalBuffer
    {
        public SafeStringBuffer(string str) : base(Encoding.Unicode.GetBytes(str + "\0"))
        {
        }
    }

    public sealed class SafeSecurityIdentifierHandle : SafeHGlobalBuffer
    {
        private static byte[] SidToArray(SecurityIdentifier sid)
        {
            byte[] ret = new byte[sid.BinaryLength];
            sid.GetBinaryForm(ret, 0);
            return ret;
        }

        public SafeSecurityIdentifierHandle(SecurityIdentifier sid) : base(SidToArray(sid))
        {
        }
    }

    public sealed class SafeSecurityDescriptor : SafeHGlobalBuffer
    {
        private static byte[] SdToArray(GenericSecurityDescriptor sd)
        {
            byte[] ret = new byte[sd.BinaryLength];
            sd.GetBinaryForm(ret, 0);
            return ret;
        }

        public SafeSecurityDescriptor(GenericSecurityDescriptor sd) : base(SdToArray(sd))
        {
        }
    }

    public sealed class SafeLocalAllocHandle : SafeHandle
    {
        [DllImport("kernel32.dll", SetLastError =true)]
        static extern IntPtr LocalFree(IntPtr hMem);

        protected override bool ReleaseHandle()
        {
            return LocalFree(handle) == IntPtr.Zero;
        }

        public SafeLocalAllocHandle(IntPtr handle, bool owns_handle) : base(IntPtr.Zero, owns_handle)
        {
            SetHandle(handle);
        }

        public SafeLocalAllocHandle() : base(IntPtr.Zero, true)
        {
        }

        public override bool IsInvalid
        {
            get
            {
                return handle == IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Safe buffer to contain a list of structures.
    /// </summary>
    public class SafeArrayBuffer<T> : SafeHGlobalBuffer
    {
        public int Count { get; private set; }

        private static int _element_size = Marshal.SizeOf(typeof(T));

        static int GetArraySize(T[] array)
        {
            return _element_size * array.Length;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="array">Array of elements.</param>
        public SafeArrayBuffer(T[] array) 
            : base(GetArraySize(array))
        {
            Count = array.Length;
            IntPtr ptr = DangerousGetHandle();
            for (int i = 0; i < array.Length; ++i)
            {
                Marshal.StructureToPtr(array[i], ptr + (i * _element_size), false);
            }
        }

        /// <summary>
        /// Dispose buffer.
        /// </summary>
        /// <param name="disposing">True if disposing.</param>
        protected override void Dispose(bool disposing)
        {
            IntPtr ptr = DangerousGetHandle();
            for (int i = 0; i < Count; ++i)
            {
                Marshal.DestroyStructure(ptr + (i * _element_size), typeof(T));
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Safe SID buffer.
    /// </summary>
    /// <remarks>This is used to return values from the RTL apis which need to be freed using RtlFreeSid</remarks>
    public sealed class SafeSidBufferHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeSidBufferHandle(IntPtr sid, bool owns_handle) : base(owns_handle)
        {
            SetHandle(sid);
        }

        public SafeSidBufferHandle() : base(true)
        {
        }

        public static SafeSidBufferHandle Null { get
            { return new SafeSidBufferHandle(IntPtr.Zero, false); }
        }

        public int Length
        {
            get { return NtRtl.RtlLengthSid(handle); }
        }

        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                NtRtl.RtlFreeSid(handle);
                handle = IntPtr.Zero;
            }
            return true;
        }
    }

    public class SafeSecurityObjectBuffer : SafeBuffer
    {
        public SafeSecurityObjectBuffer() : base(true)
        {
            Initialize(0);
        }

        protected override bool ReleaseHandle()
        {
            return NtRtl.RtlDeleteSecurityObject(ref handle).IsSuccess();
        }
    }

    public class SafeIoStatusBuffer : SafeStructureInOutBuffer<IoStatus>
    {
    }

    /// <summary>
    /// Some simple utilities to create structure buffers.
    /// </summary>
    public static class BufferUtils
    {
        /// <summary>
        /// Create a buffer based on a passed type.
        /// </summary>
        /// <typeparam name="T">The type to use in the structure buffer.</typeparam>
        /// <param name="value">The value to initialize the buffer with.</param>
        /// <param name="additional_size">Additional byte data after the structure.</param>
        /// <param name="add_struct_size">Indicates if additional_size includes the structure size or not.</param>
        /// <returns>The new structure buffer.</returns>
        public static SafeStructureInOutBuffer<T> CreateBuffer<T>(T value, int additional_size, bool add_struct_size) where T : new()
        {
            return new SafeStructureInOutBuffer<T>(value, additional_size, add_struct_size);
        }

        /// <summary>
        /// Create a buffer based on a passed type.
        /// </summary>
        /// <typeparam name="T">The type to use in the structure buffer.</typeparam>
        /// <param name="value">The value to initialize the buffer with.</param>
        /// <returns>The new structure buffer.</returns>
        public static SafeStructureInOutBuffer<T> CreateBuffer<T>(T value) where T : new()
        {
            return new SafeStructureInOutBuffer<T>(value);
        }

        /// <summary>
        /// Create a buffer based on a passed type.
        /// </summary>
        /// <typeparam name="T">The type to use in the structure buffer.</typeparam>
        /// <param name="value">The value to initialize the buffer with.</param>
        /// <returns>The new structure buffer.</returns>
        public static SafeStructureInOutBuffer<T> ToBuffer<T>(this T value) where T : new()
        {
            return CreateBuffer(value, 0, true);
        }

        /// <summary>
        /// Create a buffer based on a passed type.
        /// </summary>
        /// <typeparam name="T">The type to use in the structure buffer.</typeparam>
        /// <param name="value">The value to initialize the buffer with.</param>
        /// <param name="additional_size">Additional byte data after the structure.</param>
        /// <param name="add_struct_size">Indicates if additional_size includes the structure size or not.</param>
        /// <returns>The new structure buffer.</returns>
        public static SafeStructureInOutBuffer<T> ToBuffer<T>(this T value, int additional_size, bool add_struct_size) where T : new()
        {
            return CreateBuffer(value, additional_size, add_struct_size);
        }

        /// <summary>
        /// Create a buffer based on a byte array.
        /// </summary>
        /// <param name="value">The byte array for the buffer.</param>
        /// <returns>The safe buffer.</returns>
        public static SafeHGlobalBuffer ToBuffer(this byte[] value)
        {
            if (value == null)
            {
                return SafeHGlobalBuffer.Null;
            }
            return new SafeHGlobalBuffer(value);
        }

        /// <summary>
        /// Create an array buffer from the array.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="value">The array of elements.</param>
        /// <returns>The allocated array buffer.</returns>
        public static SafeArrayBuffer<T> ToArrayBuffer<T>(this T[] value)
        {
            return new SafeArrayBuffer<T>(value);
        }

        internal static DataStartAttribute GetStructDataAttribute<T>() where T : new()
        {
            return typeof(T).GetCustomAttribute<DataStartAttribute>();
        }

        internal static int GetStructDataOffset<T>() where T : new()
        {
            var attr = GetStructDataAttribute<T>();
            if (attr != null)
            {
                return Marshal.OffsetOf(typeof(T), attr.FieldName).ToInt32();
            }
            return Marshal.SizeOf(typeof(T));
        }

        internal static bool GetIncludeField<T>() where T : new()
        {
            var attr = GetStructDataAttribute<T>();
            if (attr != null)
            {
                return attr.IncludeDataField;
            }
            return true;
        }

        /// <summary>
        /// Read a NUL terminated string for the byte offset.
        /// </summary>
        /// <param name="buffer">The buffer to read from.</param>
        /// <param name="byte_offset">The byte offset to read from.</param>
        /// <returns>The string read from the buffer without the NUL terminator</returns>
        public static string ReadNulTerminatedUnicodeString(SafeBuffer buffer, ulong byte_offset)
        {
            List<char> chars = new List<char>();
            while (byte_offset < buffer.ByteLength)
            {
                char c = buffer.Read<char>(byte_offset);
                if (c == 0)
                {
                    break;
                }
                chars.Add(c);
                byte_offset += 2;
            }
            return new string(chars.ToArray());
        }

        /// <summary>
        /// Read a Unicode string string with length.
        /// </summary>
        /// <param name="buffer">The buffer to read from.</param>
        /// <param name="count">The number of characters to read.</param>
        /// <param name="byte_offset">The byte offset to read from.</param>
        /// <returns>The string read from the buffer without the NUL terminator</returns>
        public static string ReadUnicodeString(SafeBuffer buffer, ulong byte_offset, int count)
        {
            char[] ret = new char[count];
            buffer.ReadArray(byte_offset, ret, 0, count);
            return new string(ret);
        }

        /// <summary>
        /// Write unicode string.
        /// </summary>
        /// <param name="buffer">The buffer to write to.</param>
        /// <param name="byte_offset">The byte offset to write to.</param>
        /// <param name="value">The string value to write.</param>
        public static void WriteUnicodeString(SafeBuffer buffer, ulong byte_offset, string value)
        {
            char[] chars = value.ToCharArray();
            buffer.WriteArray(byte_offset, chars, 0, chars.Length);
        }

        /// <summary>
        /// Read bytes from buffer.
        /// </summary>
        /// <param name="buffer">The buffer to read from.</param>
        /// <param name="byte_offset">The byte offset to read from.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>The byte array.</returns>
        public static byte[] ReadBytes(SafeBuffer buffer, ulong byte_offset, int count)
        {
            byte[] ret = new byte[count];
            buffer.ReadArray(byte_offset, ret, 0, count);
            return ret;
        }

        /// <summary>
        /// Write bytes to a buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write to.</param>
        /// <param name="byte_offset">The byte offset to write to.</param>
        /// <param name="data">The data to write.</param>
        public static void WriteBytes(SafeBuffer buffer, ulong byte_offset, byte[] data)
        {
            buffer.WriteArray(byte_offset, data, 0, data.Length);
        }

        /// <summary>
        /// Get a structure buffer at a specific offset.
        /// </summary>
        /// <typeparam name="T">The type of structure.</typeparam>
        /// <param name="buffer">The buffer to map.</param>
        /// <param name="offset">The offset into the buffer.</param>
        /// <returns>The structure buffer.</returns>
        /// <remarks>The returned buffer is not owned, therefore you need to maintain the original buffer while operating on this buffer.</remarks>
        public static SafeStructureInOutBuffer<T> GetStructAtOffset<T>(SafeBuffer buffer, int offset) where T : new()
        {
            int length_left = (int)buffer.ByteLength - offset;
            int struct_size = Marshal.SizeOf(typeof(T));
            if (length_left < struct_size)
            {
                throw new ArgumentException("Invalid length for structure");
            }

            return new SafeStructureInOutBuffer<T>(buffer.DangerousGetHandle() + offset, length_left, false);
        }

        /// <summary>
        /// Creates a view of an existing safe buffer.
        /// </summary>
        /// <param name="buffer">The buffer to create a view on.</param>
        /// <param name="offset">The offset from the start of the buffer.</param>
        /// <param name="length">The length of the view.</param>
        /// <returns>The buffer view.</returns>
        /// <remarks>Note that the returned buffer doesn't own the memory, therefore the original buffer
        /// must be maintained for the lifetime of this buffer.</remarks>
        public static SafeBuffer CreateBufferView(SafeBuffer buffer, int offset, int length)
        {
            long total_length = (long)buffer.ByteLength;
            if (offset + length > total_length)
            {
                throw new ArgumentException("Offset and length is larger than the existing buffer");
            }

            return new SafeHGlobalBuffer(buffer.DangerousGetHandle() + offset, length, false);
        }

        /// <summary>
        /// Zero an entire buffer.
        /// </summary>
        /// <param name="buffer">The buffer to zero.</param>
        public static void ZeroBuffer(SafeBuffer buffer)
        {
            NtRtl.RtlZeroMemory(buffer.DangerousGetHandle(), new IntPtr(buffer.GetLength()));
        }

        /// <summary>
        /// Fill an entire buffer with a specific byte value.
        /// </summary>
        /// <param name="buffer">The buffer to full.</param>
        /// <param name="fill">The fill value.</param>
        public static void FillBuffer(SafeBuffer buffer, byte fill)
        {
            NtRtl.RtlFillMemory(buffer.DangerousGetHandle(), new IntPtr(buffer.GetLength()), fill);
        }
    }

#pragma warning restore 1591
}
