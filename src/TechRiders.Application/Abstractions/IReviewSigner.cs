namespace TechRiders.Application;

public interface IReviewSigner
{
    string Sign(ReviewProof proof);
    ReviewProof Verify(string token);
}
