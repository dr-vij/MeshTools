using NUnit.Framework;
using PropellerheadMesh;
using Unity.Collections;

namespace Plugins.MeshTools.PropellerheadMesh.Scripts.Tests
{
    public class NativeArray2DTest
    {
        [Test]
        public void AddAtFunctionality()
        {
            // PART 1
            // we create a 2d array
            using var array2D = new NativeArray2D<int>(4, 3, Allocator.Temp);

            // Add 2 elements to it
            int recordIndex = array2D.CreateArrayRecord();
            Assert.AreEqual(0, recordIndex);
            array2D.Append(10);
            array2D.Append(20);

            // add 1 more element, it should not resize now.
            int resultIndex = array2D.AppendAt(recordIndex, 30);
            Assert.AreEqual(recordIndex, resultIndex);
            Assert.AreEqual(3, array2D.GetLength(recordIndex));
            Assert.AreEqual(30, array2D[recordIndex, 2]);

            // now we add an extra element that will cause the resize of the page.
            int newRecordIndex = array2D.AppendAt(recordIndex, 40);
            Assert.AreEqual(recordIndex, newRecordIndex);

            // PART 2 
            // we create one more record. and feel its page
            int oldRecordIndex = array2D.CreateArrayRecord();
            array2D.Append(100);
            array2D.Append(200);
            array2D.Append(300);

            // now we grow it no next page, but it is the last one, so record index should not change now.
            int relocatedIndex = array2D.AppendAt(oldRecordIndex, 400);
            Assert.AreEqual(oldRecordIndex, relocatedIndex);
            Assert.AreEqual(4, array2D.GetLength(relocatedIndex));
            Assert.AreEqual(100, array2D[relocatedIndex, 0]);
            Assert.AreEqual(400, array2D[relocatedIndex, 3]);
            
            // PART 3
            // first fill up the first record to capacity (it has 4 elements, capacity is 6)
            array2D.AppendAt(recordIndex, 50);
            array2D.AppendAt(recordIndex, 60);

            // now we add to the first index when it's full and not current. that will cause relocation
            int movedRecordIndex = array2D.AppendAt(recordIndex, 70);
            Assert.AreNotEqual(recordIndex, movedRecordIndex);
            Assert.AreEqual(7, array2D.GetLength(movedRecordIndex));
            Assert.AreEqual(10, array2D[movedRecordIndex, 0]);
            Assert.AreEqual(20, array2D[movedRecordIndex, 1]);
            Assert.AreEqual(30, array2D[movedRecordIndex, 2]);
            Assert.AreEqual(40, array2D[movedRecordIndex, 3]);
            Assert.AreEqual(50, array2D[movedRecordIndex, 4]);
            Assert.AreEqual(60, array2D[movedRecordIndex, 5]);
            Assert.AreEqual(70, array2D[movedRecordIndex, 6]);
            
            // Part 4 foreach enumerator test
            Assert.AreEqual(2, array2D.ActivePageCount);
            
            var enumerator = array2D.GetActivePageEnumerator();
            int activePageCount = 0;
            while (enumerator.MoveNext())
            {
                int pageIndex = enumerator.Current;
                var page = array2D.GetPageInfo(pageIndex);
                Assert.IsTrue(page.Length > 0);
                activePageCount++;
            }
            Assert.AreEqual(2, activePageCount);
            
            // Test with list
            using var activePages = new NativeList<int>(Allocator.Temp);
            array2D.GetActivePageIndices(activePages);
            Assert.AreEqual(2, activePages.Length);
            Assert.AreEqual(oldRecordIndex, activePages[0]);
            Assert.AreEqual(movedRecordIndex, activePages[1]);
            
            // Test ForEach methods
            var localArray = array2D;
            var forEachCount = 0;
            localArray.ForEachActivePage(pageIndex => {
                forEachCount++;
                Assert.IsTrue(localArray.GetLength(pageIndex) > 0);
            });
            Assert.AreEqual(2, forEachCount);
            
            var sliceCount = 0;
            localArray.ForEachActivePageSlice((pageIndex, slice) => {
                sliceCount++;
                Assert.IsTrue(slice.Length > 0);
            });
            Assert.AreEqual(2, sliceCount);
        }
    }
}