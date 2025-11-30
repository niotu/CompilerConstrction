import pefile
import sys

pe_path = "out_srm_print.dll"

try:
    pe = pefile.PE(pe_path)
    print(f"[PE] File: {pe_path}")
    print(f"  Entry point: 0x{pe.OPTIONAL_HEADER.AddressOfEntryPoint:X}")
    print(f"  Sections:")
    for section in pe.sections:
        print(f"    {section.Name.decode().rstrip(chr(0))}  VA: 0x{section.VirtualAddress:X}  Size: {section.SizeOfRawData}")
    print(f"  Number of sections: {len(pe.sections)}")
    print(f"  Data directories:")
    for i, dd in enumerate(pe.OPTIONAL_HEADER.DATA_DIRECTORY):
        print(f"    {pefile.DIRECTORY_ENTRY[i]}: VA=0x{dd.VirtualAddress:X} Size={dd.Size}")
    # Check for .NET metadata
    if hasattr(pe, 'DIRECTORY_ENTRY_COM_DESCRIPTOR'):
        print("  [OK] .NET COM Descriptor found!")
    else:
        print("  [WARN] .NET COM Descriptor not found!")
except Exception as e:
    print(f"[ERR] Could not parse PE file: {e}")
