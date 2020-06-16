//
// HashAlgoFactory.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2009 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography;

namespace MonoTorrent
{
    // FIXME: Make this internal in the future
    [EditorBrowsable (EditorBrowsableState.Never)]
    public static class HashAlgoFactory
    {
        // See https://gist.github.com/mairaw/c5ea693b956b584f44c3423e29e1f0c6

        public static Func<HashAlgorithm> SHA1Builder = () => SHA1.Create ();
        public static Func<HashAlgorithm> MD5Builder = () => MD5.Create ();

        public static T Create<T> ()
            where T : HashAlgorithm
        {
            if (typeof(T) == typeof(SHA1)) {
                return (T) SHA1Builder ();
            }

            if (typeof (T) == typeof (MD5)) {
                return (T) MD5Builder ();
            }

            throw new NotSupportedException ($"{typeof (T)} hash algorithm is not supported");
        }
    }
}
