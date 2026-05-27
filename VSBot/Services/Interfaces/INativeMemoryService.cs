namespace VSBot.Services.Interfaces
{
    public interface INativeMemoryService
    {
        /// <summary>
        /// Allocates a block of memory with read, write, and execute protection.
        /// </summary>
        /// <param name="size">The size in bytes to allocate.</param>
        /// <returns>A pointer to the allocated memory block, or <see cref="nint.Zero"/> on failure.</returns>
        nint AllocateMemory(int size);

        /// <summary>
        /// Frees a block of memory previously allocated by <see cref="AllocateMemory"/>.
        /// </summary>
        /// <param name="address">The pointer returned by <see cref="AllocateMemory"/>.</param>
        void FreeMemory(nint address);

        /// <summary>
        /// Changes the memory protection of a region to allow read, write, and execute access.
        /// </summary>
        /// <param name="address">The base address of the memory region to protect.</param>
        /// <param name="size">The size in bytes of the region.</param>
        /// <returns><c>true</c> if the protection was updated successfully; otherwise, <c>false</c>.</returns>
        bool SetMemoryProtection(nint address, int size);

        /// <summary>
        /// Writes a byte array to the specified memory address.
        /// </summary>
        /// <param name="address">The target memory address.</param>
        /// <param name="data">The bytes to write.</param>
        void WriteBytes(nint address, byte[] data);

        /// <summary>
        /// Writes a 32-bit signed integer at <paramref name="address"/> plus <paramref name="offset"/>.
        /// </summary>
        /// <param name="address">The base memory address.</param>
        /// <param name="offset">The byte offset from the base address.</param>
        /// <param name="value">The value to write.</param>
        void WriteInt32(nint address, int offset, int value);

        /// <summary>
        /// Writes a native pointer value at <paramref name="address"/> plus <paramref name="offset"/>.
        /// </summary>
        /// <param name="address">The base memory address.</param>
        /// <param name="offset">The byte offset from the base address.</param>
        /// <param name="value">The pointer value to write.</param>
        void WriteIntPtr(nint address, int offset, nint value);

        /// <summary>
        /// Reads a sequence of bytes from the specified memory address.
        /// </summary>
        /// <param name="address">The source memory address.</param>
        /// <param name="length">The number of bytes to read.</param>
        /// <returns>A byte array containing the bytes read from memory.</returns>
        byte[] ReadBytes(nint address, int length);
    }
}
