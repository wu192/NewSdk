/*
 * Copyright (C) 2026 JPEXS.
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston,
 * MA 02110-1301  USA
 */
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace BinaryAssetBuilder.UiCompiler.Fla
{



internal class OleRecordDecoder {

    private OleRecordDecoder() {
    }

    internal static void item(MfcArchive a, FlaRecord r) {
        long signature = a.readUI32();
        r.set("signature", signature);
        if (signature != 0x100) {
            throw a.error("Invalid OLE client-item signature");
        }
        r.set("itemNumber", a.readUI32());
        r.set("value1", a.readUI32());
        r.set("monikerPresent", a.readUI16());
        r.set("drawAspect", a.readUI32());
        
        r.set("revision", a.readUI8());
        r.set("oleItemId", a.readUI16());
        a.readObjectInto(r, "documentPage");
    }
}

}
