using Plugin.Firebase.Auth;
using Plugin.Firebase.Firestore;

namespace MobiHymn4.Services;

public interface IFirebaseFirestoreAccessor
{
    IFirebaseFirestore Firestore { get; }
    IFirebaseAuth Auth { get; }
}

public sealed class FirebaseFirestoreAccessor : IFirebaseFirestoreAccessor
{
    public IFirebaseFirestore Firestore => CrossFirebaseFirestore.Current;
    public IFirebaseAuth Auth => CrossFirebaseAuth.Current;
}
