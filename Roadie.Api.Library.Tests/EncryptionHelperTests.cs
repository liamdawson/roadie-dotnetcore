﻿using Roadie.Library.Utility;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Roadie.Library.Tests
{
    public class EncryptionHelperTests
    {
        [Fact]
        public void Encrypt_And_Decrypt()
        {
            var key = Guid.NewGuid().ToString();
            var value = Guid.NewGuid().ToString();

            var encrypted = EncryptionHelper.Encrypt(value, key);
            Assert.NotNull(encrypted);
            Assert.NotEqual(encrypted, value);

            var decrypted = EncryptionHelper.Decrypt(encrypted, key);
            Assert.NotNull(decrypted);
            Assert.Equal(decrypted, value);

        }

    }
}
